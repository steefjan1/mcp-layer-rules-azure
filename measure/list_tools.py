#!/usr/bin/env python3
"""
Rule 3 and rule 4, measured instead of assumed.

Live mode talks MCP (streamable HTTP) to the deployed or local server, runs
initialize + tools/list, and reports per tool: name, whether `annotations`
(readOnlyHint / destructiveHint ...) came back at all, and the token cost of the
definition as the model will see it.

Offline mode parses src/StayMcp/Tools/StayTools.cs and builds the same report from
the attributes, plus a comparison with the "one tool per endpoint" shape the
infographic warns against (rule 2), so CI can fail if the surface grows.

    python measure/list_tools.py --url http://localhost:7072/runtime/webhooks/mcp
    python measure/list_tools.py --url https://func-staymcp-xxx.azurewebsites.net/runtime/webhooks/mcp --key <mcp_extension key>
    python measure/list_tools.py --offline src/StayMcp/Tools/StayTools.cs

Token counts use tiktoken (cl100k_base) when installed, otherwise a 4-chars-per-token
estimate, and the report says which. Stdlib only apart from that optional import.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import urllib.request

ANNOTATION_KEYS = ("readOnlyHint", "destructiveHint", "idempotentHint", "openWorldHint", "title")


# ---------- token counting ----------

def make_counter():
    try:
        import tiktoken  # type: ignore
        enc = tiktoken.get_encoding("cl100k_base")
        return (lambda s: len(enc.encode(s))), "tiktoken cl100k_base"
    except Exception:
        return (lambda s: max(1, round(len(s) / 4))), "estimate (len/4)"


def tool_tokens(tool: dict, count) -> int:
    # What the client puts in front of the model: the JSON definition, compact.
    return count(json.dumps(tool, separators=(",", ":"), ensure_ascii=False))


# ---------- live MCP ----------

def mcp_call(url: str, key: str | None, session: str | None, payload: dict) -> tuple[dict | None, str | None]:
    body = json.dumps(payload).encode()
    req = urllib.request.Request(url, data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Accept", "application/json, text/event-stream")
    if key:
        req.add_header("x-functions-key", key)
    if session:
        req.add_header("Mcp-Session-Id", session)
    with urllib.request.urlopen(req, timeout=60) as resp:
        sid = resp.headers.get("Mcp-Session-Id", session)
        ctype = resp.headers.get("Content-Type", "")
        raw = resp.read().decode("utf-8", "replace")
    if "text/event-stream" in ctype:
        for line in raw.splitlines():
            if line.startswith("data:"):
                data = line[5:].strip()
                if data:
                    msg = json.loads(data)
                    if msg.get("id") == payload.get("id"):
                        return msg, sid
        return None, sid
    return (json.loads(raw) if raw.strip() else None), sid


def live_tools(url: str, key: str | None) -> list[dict]:
    init = {
        "jsonrpc": "2.0", "id": 1, "method": "initialize",
        "params": {"protocolVersion": "2025-06-18", "capabilities": {},
                   "clientInfo": {"name": "list_tools.py", "version": "1.0"}},
    }
    msg, sid = mcp_call(url, key, None, init)
    if msg and "result" in msg:
        info = msg["result"].get("serverInfo", {})
        print(f"server: {info.get('name')} {info.get('version')}  protocol {msg['result'].get('protocolVersion')}")
    try:
        mcp_call(url, key, sid, {"jsonrpc": "2.0", "method": "notifications/initialized"})
    except Exception:
        pass  # some servers answer 202 with an empty body; urllib treats that as fine, others may 4xx
    msg, _ = mcp_call(url, key, sid, {"jsonrpc": "2.0", "id": 2, "method": "tools/list"})
    if not msg or "result" not in msg:
        raise SystemExit(f"tools/list failed: {msg}")
    return msg["result"]["tools"]


# ---------- offline from source ----------

ATTR_RE = re.compile(
    r'\[McpToolTrigger\("(?P<name>[^"]+)",\s*"(?P<desc>(?:[^"\\]|\\.)*)"\)\]'
    r'(?P<body>.*?)(?=\[Function\(|\Z)', re.S)
# [McpToolProperty("name", "description", isRequired: true)] string name   -> type from the C# parameter
PROP_RE = re.compile(r'\[McpToolProperty\("(?P<name>[^"]+)",\s*"(?P<desc>(?:[^"\\]|\\.)*)"(?:,\s*isRequired:\s*(?P<req>true|false))?\)\]\s*(?P<cstype>[\w?]+)\s+\w+')
CS_TYPES = {"string": "string", "int": "integer", "long": "integer", "double": "number", "decimal": "number", "bool": "boolean"}


def offline_tools(path: str) -> list[dict]:
    src = open(path, encoding="utf-8").read()
    tools = []
    for m in ATTR_RE.finditer(src):
        props = {}
        required = []
        for p in PROP_RE.finditer(m.group("body")):
            cstype = p.group("cstype").rstrip("?")
            props[p.group("name")] = {"type": CS_TYPES.get(cstype, "object"), "description": p.group("desc")}
            if p.group("req") == "true":
                required.append(p.group("name"))
        tools.append({
            "name": m.group("name"),
            "description": m.group("desc"),
            "inputSchema": {"type": "object", "properties": props, "required": required},
        })
    return tools


# The shape the infographic warns against: one tool per API route.
ENDPOINT_SHAPED = [
    ("list_listings", "GET /listings. Lists listings, optionally filtered by city and guests.", {"city": "string", "guests": "integer"}),
    ("create_quote", "POST /quotes. Creates a quote for a listing and dates.", {"listingId": "string", "checkIn": "string", "checkOut": "string", "guests": "integer"}),
    ("create_booking", "POST /bookings. Creates a booking from a signed quote.", {"signedQuote": "string", "guestName": "string", "idempotencyKey": "string"}),
    ("get_booking", "GET /bookings/{id}. Gets one booking by id.", {"bookingId": "string"}),
    ("delete_booking", "DELETE /bookings/{id}. Deletes a booking by id.", {"bookingId": "string"}),
    ("get_signing_keys", "GET /.well-known/stay-keys. Returns the quote signing public keys.", {}),
]


def endpoint_shaped() -> list[dict]:
    out = []
    for name, desc, props in ENDPOINT_SHAPED:
        out.append({
            "name": name, "description": desc,
            "inputSchema": {"type": "object",
                            "properties": {k: {"type": v, "description": k} for k, v in props.items()},
                            "required": list(props)},
        })
    return out


# ---------- report ----------

def report(tools: list[dict], count, how: str, budget: int | None) -> int:
    print(f"\n{'tool':<16}{'tokens':>7}  annotations            description")
    total = 0
    missing = []
    for t in tools:
        n = tool_tokens(t, count)
        total += n
        ann = t.get("annotations")
        if ann:
            flags = ",".join(f"{k}={ann[k]}" for k in ANNOTATION_KEYS if k in ann)
        else:
            flags = "(none returned)"
            missing.append(t["name"])
        first = t.get("description", "").split(".")[0]
        print(f"{t['name']:<16}{n:>7}  {flags:<22} {first}")
    print(f"{'total':<16}{total:>7}   ({len(tools)} tools, {how})")

    if missing:
        print(f"\nrule 4: {len(missing)} of {len(tools)} tools carry no annotations on the wire -> "
              "the client cannot tell reads from writes from metadata; the confirmation has to live in the "
              "tool contract (confirm=true) and in the API's role checks.")
    else:
        print("\nrule 4: every tool carries annotations.")

    rc = 0
    if budget is not None and total > budget:
        print(f"\nrule 3: FAIL, tools/list costs {total} tokens, budget is {budget}.")
        rc = 1
    elif budget is not None:
        print(f"\nrule 3: ok, {total} tokens of {budget} budget.")
    return rc


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", help="MCP endpoint, e.g. http://localhost:7072/runtime/webhooks/mcp")
    ap.add_argument("--key", help="x-functions-key (mcp_extension system key) for an Azure-hosted server")
    ap.add_argument("--offline", metavar="STAYTOOLS_CS", help="parse the C# source instead of calling a server")
    ap.add_argument("--budget", type=int, default=1200, help="max tokens for the whole tools/list (default 1200)")
    ap.add_argument("--json", action="store_true", help="also dump tools/list as JSON to stdout")
    a = ap.parse_args()

    count, how = make_counter()

    if a.offline:
        tools = offline_tools(a.offline)
        print(f"offline: {len(tools)} tools parsed from {a.offline}")
        print("(annotations are reported as absent: the McpToolTrigger attribute has no field for them)")
        rc = report(tools, count, how, a.budget)
        ep = endpoint_shaped()
        ep_total = sum(tool_tokens(t, count) for t in ep)
        task_total = sum(tool_tokens(t, count) for t in tools)
        print(f"\nrule 2 vs rule 3: task-shaped {len(tools)} tools = {task_total} tokens; "
              f"endpoint-shaped {len(ep)} tools with one-line descriptions = {ep_total} tokens. "
              "Fewer tools does not automatically mean fewer tokens: the task shape spends tokens on "
              "when-to-call prose and buys fewer calls per task; the endpoint shape leaves the "
              "quote-then-book sequence and the idempotency key to the model.")
    elif a.url:
        tools = live_tools(a.url, a.key)
        rc = report(tools, count, how, a.budget)
    else:
        ap.error("give --url or --offline")
        return 2

    if a.json:
        print(json.dumps(tools, indent=2))
    return rc


if __name__ == "__main__":
    sys.exit(main())
