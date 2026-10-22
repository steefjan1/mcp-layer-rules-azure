#!/usr/bin/env python3
"""
Walks the model door end to end over MCP and prints what each tool returned.

    python measure/mcp_smoke.py --url http://localhost:7072/runtime/webhooks/mcp
    python measure/mcp_smoke.py --url https://func-staymcp-xxx.azurewebsites.net/runtime/webhooks/mcp --key <mcp_extension>

Sequence and what each step proves:
  1. find_stays            the MCP app's own token reaches the API and Stays.Read is honoured
  2. quote_stay            a signed quote comes back
  3. book_stay, tampered   the MCP server refuses before calling the API (provenance, agent side)
  4. book_stay, no confirm refused by the tool contract
  5. book_stay             booking created (Bookings.Create)
  6. book_stay again       same booking, no second write (idempotency)
  7. cancel_stay           forbidden prose unless the identity holds Bookings.Cancel (authority)
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from list_tools import mcp_call  # noqa: E402


class Client:
    def __init__(self, url: str, key: str | None):
        self.url, self.key, self.sid, self.n = url, key, None, 0
        msg, self.sid = mcp_call(url, key, None, {
            "jsonrpc": "2.0", "id": self._id(), "method": "initialize",
            "params": {"protocolVersion": "2025-06-18", "capabilities": {},
                       "clientInfo": {"name": "mcp_smoke.py", "version": "1.0"}}})
        try:
            mcp_call(url, key, self.sid, {"jsonrpc": "2.0", "method": "notifications/initialized"})
        except Exception:
            pass

    def _id(self) -> int:
        self.n += 1
        return self.n

    def call(self, name: str, args: dict) -> str:
        msg, _ = mcp_call(self.url, self.key, self.sid, {
            "jsonrpc": "2.0", "id": self._id(), "method": "tools/call",
            "params": {"name": name, "arguments": args}})
        if not msg or "result" not in msg:
            return f"<error: {json.dumps(msg)[:300]}>"
        parts = msg["result"].get("content", [])
        return "\n".join(p.get("text", "") for p in parts if p.get("type") == "text")


def step(title: str, text: str, expect: str) -> bool:
    ok = expect.lower() in text.lower()
    flag = "PASS" if ok else "FAIL"
    print(f"\n[{flag}] {title}  (expected to contain: {expect!r})")
    print("      " + text.replace("\n", "\n      ")[:900])
    return ok


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", required=True)
    ap.add_argument("--key")
    ap.add_argument("--city", default="Amsterdam")
    ap.add_argument("--check-in", default="2026-12-05")
    ap.add_argument("--check-out", default="2026-12-08")
    ap.add_argument("--guests", type=int, default=4)
    ap.add_argument("--expect-cancel", choices=["forbidden", "cancelled"], default="forbidden",
                    help="what cancel_stay should do; 'cancelled' if the MCP identity holds Bookings.Cancel")
    a = ap.parse_args()

    c = Client(a.url, a.key)
    results = []

    r = c.call("find_stays", {"city": a.city, "guests": a.guests})
    results.append(step("1. find_stays", r, "nightlyRate"))
    try:
        listings = json.loads(r)
        listing_id = listings[0]["id"]
    except Exception:
        print("\ncannot continue without a listing id")
        return 1

    r = c.call("quote_stay", {"listingId": listing_id, "checkIn": a.check_in, "checkOut": a.check_out, "guests": a.guests})
    results.append(step("2. quote_stay", r, "signature"))
    try:
        signed = json.loads(r)
    except Exception:
        print("\ncannot continue without a signed quote")
        return 1

    tampered = json.loads(json.dumps(signed))
    tampered["quote"]["total"] = 1
    r = c.call("book_stay", {"signedQuote": json.dumps(tampered), "guestName": "Mallory", "confirm": True})
    results.append(step("3. book_stay with a tampered total", r, "not executed"))

    r = c.call("book_stay", {"signedQuote": json.dumps(signed), "guestName": "Alice", "confirm": False})
    results.append(step("4. book_stay without confirm", r, "not executed"))

    r = c.call("book_stay", {"signedQuote": json.dumps(signed), "guestName": "Alice", "confirm": True})
    results.append(step("5. book_stay with confirm", r, "bookingId"))
    booking_id = None
    try:
        booking_id = json.loads(r)["bookingId"]
    except Exception:
        pass

    r = c.call("book_stay", {"signedQuote": json.dumps(signed), "guestName": "Alice", "confirm": True})
    results.append(step("6. book_stay again, same quote", r, "already booked"))

    if booking_id:
        r = c.call("cancel_stay", {"bookingId": booking_id, "confirm": True})
        results.append(step("7. cancel_stay", r, a.expect_cancel))

    print(f"\n{sum(results)} of {len(results)} steps behaved as expected.")
    return 0 if all(results) else 1


if __name__ == "__main__":
    sys.exit(main())
