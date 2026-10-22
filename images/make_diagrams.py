#!/usr/bin/env python3
"""
Draws the two post diagrams as SVG and PNG.

    python images/make_diagrams.py

Icons: if images/icons/<name>.svg exists (official Azure Architecture Icons, e.g.
functions.svg, entra-id.svg, managed-identity.svg, table-storage.svg, app-insights.svg,
key-vault.svg, foundry.svg, apim.svg) it is embedded; otherwise a simple drawn glyph in the
Azure palette stands in. PNG export needs cairosvg (pip install cairosvg).
"""
from __future__ import annotations

import base64
import os
from pathlib import Path

HERE = Path(__file__).parent
ICONS = HERE / "icons"

# palette (Azure-ish, print-safe)
BLUE = "#0078D4"
NAVY = "#1F2937"
INK = "#111827"
GRAY = "#6B7280"
LINE = "#9CA3AF"
PANEL = "#F3F4F6"
WHITE = "#FFFFFF"
GREEN = "#15803D"
GREEN_BG = "#DCFCE7"
AMBER = "#B45309"
AMBER_BG = "#FEF3C7"
RED = "#B91C1C"
RED_BG = "#FEE2E2"
FUNC_Y = "#FFB900"
FONT = "Segoe UI, Roboto, Helvetica, Arial, sans-serif"
MONO = "Consolas, 'Cascadia Mono', 'Courier New', monospace"


def esc(s: str) -> str:
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


# ---------- icons ----------

def icon(name: str, x: float, y: float, size: float = 40) -> str:
    f = ICONS / f"{name}.svg"
    if f.exists():
        data = base64.b64encode(f.read_bytes()).decode()
        return f'<image x="{x}" y="{y}" width="{size}" height="{size}" href="data:image/svg+xml;base64,{data}"/>'
    g = GLYPHS.get(name, glyph_generic)
    return f'<g transform="translate({x},{y}) scale({size / 40})">{g()}</g>'


def glyph_functions() -> str:
    return (f'<rect x="2" y="2" width="36" height="36" rx="6" fill="{BLUE}"/>'
            f'<path d="M23 5 L11 22 h8 l-3 13 L30 18 h-8 z" fill="{FUNC_Y}"/>')


def glyph_entra() -> str:
    return (f'<path d="M20 3 L36 20 L20 37 L4 20 Z" fill="#0F6CBD"/>'
            f'<path d="M20 3 L36 20 L20 20 Z" fill="#50E6FF"/>'
            f'<circle cx="20" cy="24" r="5" fill="{WHITE}"/>')


def glyph_identity() -> str:
    return (f'<rect x="3" y="6" width="34" height="28" rx="4" fill="#5C2D91"/>'
            f'<circle cx="14" cy="17" r="5" fill="{WHITE}"/>'
            f'<rect x="7" y="23" width="14" height="7" rx="3" fill="{WHITE}"/>'
            f'<rect x="24" y="13" width="10" height="3" fill="#C7B3E6"/>'
            f'<rect x="24" y="19" width="10" height="3" fill="#C7B3E6"/>'
            f'<rect x="24" y="25" width="10" height="3" fill="#C7B3E6"/>')


def glyph_table() -> str:
    return (f'<rect x="3" y="7" width="34" height="26" rx="3" fill="#0078D4"/>'
            f'<rect x="3" y="7" width="34" height="7" fill="#005BA1"/>'
            f'<line x1="14" y1="14" x2="14" y2="33" stroke="{WHITE}" stroke-width="1.5"/>'
            f'<line x1="26" y1="14" x2="26" y2="33" stroke="{WHITE}" stroke-width="1.5"/>'
            f'<line x1="3" y1="20" x2="37" y2="20" stroke="{WHITE}" stroke-width="1.5"/>'
            f'<line x1="3" y1="26" x2="37" y2="26" stroke="{WHITE}" stroke-width="1.5"/>')


def glyph_insights() -> str:
    return (f'<path d="M20 3 a13 13 0 0 1 8 23 v6 h-16 v-6 a13 13 0 0 1 8 -23 z" fill="#68217A"/>'
            f'<rect x="14" y="33" width="12" height="4" rx="2" fill="#68217A"/>'
            f'<path d="M15 17 l4 4 l6 -8" fill="none" stroke="{WHITE}" stroke-width="2.5"/>')


def glyph_key() -> str:
    return (f'<circle cx="14" cy="14" r="9" fill="none" stroke="#5C2D91" stroke-width="4"/>'
            f'<path d="M20 20 L34 34 M28 28 l4 -4 M31 31 l4 -4" stroke="#5C2D91" stroke-width="4" fill="none" stroke-linecap="round"/>')


def glyph_agent() -> str:
    return (f'<rect x="4" y="8" width="32" height="24" rx="6" fill="{NAVY}"/>'
            f'<circle cx="14" cy="20" r="4" fill="#50E6FF"/><circle cx="26" cy="20" r="4" fill="#50E6FF"/>'
            f'<rect x="18" y="2" width="4" height="6" fill="{NAVY}"/>')


def glyph_vscode() -> str:
    return (f'<path d="M28 4 L36 8 V32 L28 36 L12 22 L6 27 L3 24 V16 L6 13 L12 18 Z" fill="#0066B8"/>'
            f'<path d="M28 4 L36 8 V32 L28 36 L28 26 L20 20 L28 14 Z" fill="#1F9CF0"/>')


def glyph_foundry() -> str:
    return (f'<rect x="3" y="3" width="34" height="34" rx="8" fill="#0F6CBD"/>'
            f'<circle cx="20" cy="20" r="9" fill="none" stroke="{WHITE}" stroke-width="3"/>'
            f'<circle cx="20" cy="20" r="3" fill="{WHITE}"/>')


def glyph_claude() -> str:
    return (f'<circle cx="20" cy="20" r="17" fill="#D97757"/>'
            f'<path d="M12 20 h16 M20 12 v16 M14 14 l12 12 M26 14 l-12 12" stroke="{WHITE}" stroke-width="3" stroke-linecap="round"/>')


def glyph_web() -> str:
    return (f'<rect x="3" y="6" width="34" height="24" rx="3" fill="{BLUE}"/>'
            f'<rect x="12" y="32" width="16" height="3" fill="{BLUE}"/>'
            f'<rect x="7" y="10" width="26" height="16" fill="{WHITE}"/>')


def glyph_mobile() -> str:
    return (f'<rect x="11" y="2" width="18" height="36" rx="4" fill="{BLUE}"/>'
            f'<rect x="14" y="7" width="12" height="24" fill="{WHITE}"/>')


def glyph_logicapps() -> str:
    return (f'<rect x="3" y="3" width="34" height="34" rx="6" fill="#0078D4"/>'
            f'<path d="M12 28 L20 12 L28 28 Z" fill="none" stroke="{WHITE}" stroke-width="3" stroke-linejoin="round"/>'
            f'<circle cx="20" cy="12" r="3" fill="{WHITE}"/><circle cx="12" cy="28" r="3" fill="{WHITE}"/><circle cx="28" cy="28" r="3" fill="{WHITE}"/>')


def glyph_mcp() -> str:
    return (f'<rect x="2" y="2" width="36" height="36" rx="8" fill="{INK}"/>'
            f'<path d="M12 27 L25 14 a4 4 0 0 1 6 6 L19 32 M15 24 L26 13 M9 30 l-2 -2 L20 15" fill="none" stroke="{WHITE}" stroke-width="2.5" stroke-linecap="round"/>')


def glyph_generic() -> str:
    return f'<rect x="4" y="4" width="32" height="32" rx="6" fill="{GRAY}"/>'


GLYPHS = {
    "functions": glyph_functions, "entra-id": glyph_entra, "managed-identity": glyph_identity,
    "table-storage": glyph_table, "app-insights": glyph_insights, "key": glyph_key,
    "agent": glyph_agent, "vscode": glyph_vscode, "foundry": glyph_foundry, "claude": glyph_claude,
    "web": glyph_web, "mobile": glyph_mobile, "logic-apps": glyph_logicapps, "mcp": glyph_mcp,
}


# ---------- primitives ----------

CHAR_W = 0.54  # average glyph width as a fraction of font size (sans)
CHAR_W_MONO = 0.60


def text(x, y, s, size=14, weight="normal", fill=INK, anchor="start", mono=False) -> str:
    fam = MONO if mono else FONT
    return (f'<text x="{x}" y="{y}" font-family="{fam}" font-size="{size}" font-weight="{weight}" '
            f'fill="{fill}" text-anchor="{anchor}">{esc(s)}</text>')


def box(x, y, w, h, fill=WHITE, stroke=LINE, rx=10, sw=1.5, dash="") -> str:
    d = f' stroke-dasharray="{dash}"' if dash else ""
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"{d}/>'


def pill(x, y, label, fg, bg, size=12) -> str:
    w = len(label) * size * CHAR_W + 20
    h = size + 10
    return (f'<rect x="{x}" y="{y}" width="{w:.0f}" height="{h}" rx="{h / 2}" fill="{bg}"/>'
            + text(x + w / 2, y + size + 2, label, size, "600", fg, "middle"))


def arrow(x1, y1, x2, y2, color=NAVY, sw=2.5, dash="") -> str:
    d = f' stroke-dasharray="{dash}"' if dash else ""
    return f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="{sw}" marker-end="url(#arr)"{d}/>'


def arrow_labelled(x1, x2, y, label, size=12) -> str:
    """Arrow with its label centred above it; the caller leaves a gap wide enough."""
    return arrow(x1, y, x2, y) + text((x1 + x2) / 2, y - 10, label, size, "normal", GRAY, "middle")


def defs() -> str:
    return (f'<defs><marker id="arr" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">'
            f'<path d="M0 0 L10 5 L0 10 z" fill="{NAVY}"/></marker></defs>')


def wrap_px(s: str, max_px: float, size: float, mono=False) -> list[str]:
    cw = size * (CHAR_W_MONO if mono else CHAR_W)
    words, lines, cur = s.split(), [], ""
    for w in words:
        cand = (cur + " " + w).strip()
        if len(cand) * cw > max_px and cur:
            lines.append(cur)
            cur = w
        else:
            cur = cand
    if cur:
        lines.append(cur)
    return lines


def para(x, y, s, max_px, size=13, fill=INK, lh=1.4, weight="normal", mono=False) -> tuple[str, float]:
    """Wrapped paragraph. Returns (svg, y of the next free line)."""
    lines = wrap_px(s, max_px, size, mono)
    svg = "".join(text(x, y + i * size * lh, line, size, weight, fill, mono=mono) for i, line in enumerate(lines))
    return svg, y + len(lines) * size * lh


def multiline(x, y, s, max_px, size=13, fill=INK, lh=1.4, weight="normal") -> str:
    return para(x, y, s, max_px, size, fill, lh, weight)[0]


# ---------- diagram 1: MCP vs API on Azure ----------

def diagram_mcp_vs_api() -> str:
    W, H = 1400, 1120
    o = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" font-family="{FONT}">',
         defs(), f'<rect width="{W}" height="{H}" fill="{WHITE}"/>']

    o.append(text(W / 2, 58, "MCP vs API on Azure", 38, "800", INK, "middle"))
    o.append(text(W / 2, 90, "Not rivals. The MCP server is one more Azure Functions app that calls the API you already have.", 17, "normal", GRAY, "middle"))

    M = 40            # outer margin
    GAP = 80          # gap between boxes, room for an arrow label
    C1, C2, C3, C4 = 270, 340, 230, 210   # column widths; X1 + sum + 3 gaps + 20 fits inside the lane
    X1 = M + 20
    X2 = X1 + C1 + GAP
    X3 = X2 + C2 + GAP
    X4 = X3 + C3 + GAP
    LANE_W = W - 2 * M

    def lane_header(y, h, bg, border, title, right, bar):
        o.append(box(M, y, LANE_W, h, bg, border, 14))
        o.append(f'<path d="M{M + 14} {y} h{LANE_W - 28} a14 14 0 0 1 14 14 v32 h-{LANE_W} v-32 a14 14 0 0 1 14 -14 z" fill="{bar}"/>')
        o.append(text(M + 22, y + 32, title, 19, "700", WHITE))
        o.append(text(M + LANE_W - 22, y + 32, right, 14, "normal", "#D1D5DB", "end"))

    # ---- lane 1: MCP
    y = 125
    lane_header(y, 400, PANEL, "#D1D5DB", "MCP  ·  the door the model reads", "tools/list → tools/call  (JSON-RPC over streamable HTTP)", INK)
    by, bh = y + 70, 300
    mid = by + bh / 2

    o.append(box(X1, by, C1, bh))
    o.append(text(X1 + 18, by + 30, "Who's asking", 16, "700"))
    for i, (ic, label, sub) in enumerate([
        ("vscode", "VS Code + Copilot", "connects with Entra, keyless"),
        ("foundry", "Microsoft Foundry agent", "MCP tool on the agent"),
        ("claude", "Claude / ChatGPT", "expects OAuth + DCR"),
        ("agent", "Your own agent", "Microsoft Agent Framework"),
    ]):
        yy = by + 50 + i * 60
        o.append(icon(ic, X1 + 18, yy, 36))
        o.append(text(X1 + 66, yy + 16, label, 15, "600"))
        o.append(text(X1 + 66, yy + 34, sub, 12, "normal", GRAY))

    o.append(arrow_labelled(X1 + C1 + 8, X2 - 8, mid, "tools/call"))

    o.append(box(X2, by, C2, bh, WHITE, BLUE, 12, 2.5))
    o.append(icon("functions", X2 + 18, by + 16, 48))
    o.append(text(X2 + 76, by + 38, "MCP server", 17, "700"))
    o.append(text(X2 + 76, by + 56, "Azure Functions, Flex Consumption", 12, "normal", GRAY))
    o.append(text(X2 + 76, by + 72, "func-staymcp · MCP tool trigger", 12, "normal", GRAY, mono=True))
    tools = [("find_stays", "read", GREEN, GREEN_BG), ("quote_stay", "read", GREEN, GREEN_BG),
             ("book_stay", "write · confirm", AMBER, AMBER_BG), ("cancel_stay", "destructive · confirm", RED, RED_BG)]
    for i, (t, k, fg, bg) in enumerate(tools):
        yy = by + 96 + i * 36
        o.append(text(X2 + 22, yy + 16, t, 14, "600", INK, mono=True))
        o.append(pill(X2 + 150, yy, k, fg, bg, 12))
    o.append(icon("managed-identity", X2 + 18, by + 246, 34))
    o.append(multiline(X2 + 60, by + 262, "own managed identity, token for api://stay-api", C2 - 80, 12, GRAY))

    o.append(arrow_labelled(X2 + C2 + 8, X3 - 8, mid, "own bearer"))

    o.append(box(X3, by, C3, bh, WHITE, BLUE, 12, 2.5))
    o.append(icon("functions", X3 + 18, by + 16, 48))
    o.append(text(X3 + 76, by + 38, "Stay API", 17, "700"))
    o.append(text(X3 + 76, by + 56, "the same API", 12, "normal", GRAY))
    o.append(text(X3 + 76, by + 72, "as the lane below", 12, "normal", GRAY))
    o.append(icon("entra-id", X3 + 18, by + 104, 36))
    o.append(multiline(X3 + 64, by + 120, "built-in auth validates the token; the app checks app roles", C3 - 82, 12.5, GRAY))
    o.append(icon("key", X3 + 18, by + 196, 34))
    o.append(multiline(X3 + 64, by + 212, "signs every quote; verifies it again on booking", C3 - 82, 12.5, GRAY))

    o.append(arrow(X3 + C3 + 8, mid, X4 - 8, mid))

    o.append(box(X4, by, C4, bh))
    o.append(icon("table-storage", X4 + 18, by + 16, 44))
    o.append(text(X4 + 72, by + 38, "Your data", 16, "700"))
    o.append(text(X4 + 72, by + 56, "Table Storage", 12, "normal", GRAY))
    for i, (t, s_) in enumerate([("bookings", "the record"), ("idempotency", "key → booking"), ("audit", "requested … completed")]):
        yy = by + 90 + i * 48
        o.append(text(X4 + 18, yy + 16, t, 14, "600", INK, mono=True))
        o.append(text(X4 + 18, yy + 34, s_, 12, "normal", GRAY))
    o.append(icon("app-insights", X4 + 18, by + 240, 34))
    o.append(text(X4 + 60, by + 262, "App Insights", 12.5, "normal", GRAY))

    # ---- lane 2: API
    y = 560
    lane_header(y, 330, "#EFF6FF", "#BFDBFE", "API  ·  how software talks to software", "HTTPS, REST/JSON, each call written by a developer", BLUE)
    by, bh = y + 70, 230
    mid = by + bh / 2

    o.append(box(X1, by, C1, bh))
    o.append(text(X1 + 18, by + 30, "Who's asking", 16, "700"))
    for i, (ic, label, sub) in enumerate([
        ("web", "Web app", "user token, delegated"),
        ("mobile", "Mobile app", "user token, delegated"),
        ("logic-apps", "Logic Apps / backend", "managed identity, app roles"),
    ]):
        yy = by + 50 + i * 58
        o.append(icon(ic, X1 + 18, yy, 36))
        o.append(text(X1 + 66, yy + 16, label, 15, "600"))
        o.append(text(X1 + 66, yy + 34, sub, 12, "normal", GRAY))

    o.append(arrow_labelled(X1 + C1 + 8, X2 - 8, mid, "bearer"))

    api_w = X3 + C3 - X2
    o.append(box(X2, by, api_w, bh, WHITE, BLUE, 12, 2.5))
    o.append(icon("functions", X2 + 18, by + 16, 48))
    o.append(text(X2 + 76, by + 38, "Stay API", 17, "700"))
    o.append(text(X2 + 76, by + 56, "Azure Functions HTTP trigger · func-stayapi · Entra built-in authentication", 12, "normal", GRAY))
    routes = [("GET  /listings", "Stays.Read"), ("POST /quotes", "Stays.Read → signed quote"),
              ("POST /bookings", "Bookings.Create · Idempotency-Key · signature"),
              ("DELETE /bookings/{id}", "Bookings.Cancel"), ("GET  /.well-known/stay-keys", "public key, anonymous")]
    for i, (r, rule) in enumerate(routes):
        yy = by + 84 + i * 28
        o.append(text(X2 + 22, yy + 16, r, 13.5, "600", INK, mono=True))
        o.append(text(X2 + 300, yy + 16, rule, 13, "normal", GRAY))

    o.append(arrow(X3 + C3 + 8, mid, X4 - 8, mid))
    o.append(box(X4, by, C4, bh))
    o.append(icon("table-storage", X4 + 18, by + 16, 44))
    o.append(text(X4 + 72, by + 38, "Your data", 16, "700"))
    o.append(text(X4 + 72, by + 56, "Table Storage", 12, "normal", GRAY))
    o.append(multiline(X4 + 18, by + 100, "Same tables, same roles, same audit rows. The door changes, the kitchen does not.", C4 - 36, 13, GRAY))

    # ---- in practice
    y = 920
    o.append(box(M, y, LANE_W, 160, WHITE, "#D1D5DB", 14))
    o.append(text(M + 22, y + 32, "IN PRACTICE", 15, "800", INK))
    chain = [("AI agent", "agent"), ("MCP server", "functions"), ("your API", "functions"), ("your data", "table-storage")]
    bw, bgap = 190, 50
    x = M + 22
    for i, (label, ic) in enumerate(chain):
        o.append(box(x, y + 50, bw, 64, PANEL, LINE, 10))
        o.append(icon(ic, x + 14, y + 62, 40))
        o.append(text(x + 66, y + 88, label, 15, "600"))
        if i < 3:
            o.append(arrow(x + bw + 6, y + 82, x + bw + bgap - 6, y + 82))
        x += bw + bgap
    o.append(text(M + 22, y + 140, "Under the hood, each MCP tool calls a regular API route with the server's own credential.", 13, "normal", GRAY))

    gx = x + 10
    gw = M + LANE_W - 22 - gx
    o.append(box(gx, y + 44, gw, 100, GREEN_BG, "#86EFAC", 10))
    o.append(multiline(gx + 14, y + 66, "Kept in the API: auth, roles, business rules, idempotency, audit. Kept in MCP: tool names, descriptions, confirm.", gw - 28, 12.5, GREEN))

    o.append(text(W - M, H - 14, "sjwiggers.com · Cloud Perspectives · mcp-layer-rules-azure", 11, "normal", LINE, "end"))
    o.append("</svg>")
    return "\n".join(o)


# ---------- diagram 2: four rules, three additions ----------

def diagram_four_rules() -> str:
    W, H = 1400, 930
    o = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" font-family="{FONT}">',
         defs(), f'<rect width="{W}" height="{H}" fill="{WHITE}"/>']
    o.append(text(W / 2, 54, "Four rules for the MCP layer, and where each one is enforced", 32, "800", INK, "middle"))
    o.append(text(W / 2, 84, "The infographic's rules in black; what the comment thread added in blue. Nothing in the MCP box decides anything.", 16, "normal", GRAY, "middle"))

    M, GAP = 40, 104
    wid = (W - 2 * M - 2 * GAP) / 3
    top, hgt = 120, 520
    PAD = 20
    inner = wid - 2 * PAD
    cols = [
        (M, "MCP client", "agent", "VS Code, Foundry agent, your own", False),
        (M + wid + GAP, "MCP server", "functions", "func-staymcp · Azure Functions", True),
        (M + 2 * (wid + GAP), "Stay API", "functions", "func-stayapi · Azure Functions", True),
    ]
    for x, title, ic, sub, mono in cols:
        stroke = BLUE if ic == "functions" else LINE
        o.append(box(x, top, wid, hgt, WHITE, stroke, 14, 2.5))
        o.append(icon(ic, x + PAD, top + 18, 48))
        o.append(text(x + PAD + 60, top + 42, title, 19, "700"))
        o.append(text(x + PAD + 60, top + 62, sub, 12.5, "normal", GRAY, mono=mono))

    HEAD, BODY = 14.5, 13
    def heading(x, y, s, fill=INK):
        o.append(text(x, y, s, HEAD, "700", fill))
        return y + 22

    # client column
    x = M + PAD
    yy = top + 100
    svg, yy = para(x, yy, "Reads tools/list. Decides which tool to call. Asks the user for a yes when the tool says so.", inner, BODY, INK); o.append(svg)
    yy += 18
    yy = heading(x, yy, "Rule 4 · label reads and writes")
    svg, yy = para(x, yy, "readOnlyHint and destructiveHint are client-side hints. The Functions MCP trigger attribute has no field for them, so this sample's tools/list carries none (measure/list_tools.py checks). The description says Read-only, Writes or Destructive, and book_stay and cancel_stay refuse without confirm=true.", inner, BODY, GRAY); o.append(svg)
    yy += 6
    o.append(pill(x, yy, "hint ≠ guarantee", AMBER, AMBER_BG)); yy += 46
    yy = heading(x, yy, "Rule 3 · expose less")
    svg, yy = para(x, yy, "Every definition sits in the model's context on every turn. Four tools here; the sixth API route is not a tool.", inner, BODY, GRAY); o.append(svg)

    # server column
    x = M + wid + GAP + PAD
    yy = top + 100
    yy = heading(x, yy, "Rule 2 · tools around tasks")
    svg, yy = para(x, yy, "find_stays → quote_stay → book_stay → cancel_stay. One tool per thing the user wants done, not one per route. GET /bookings/{id} has no tool.", inner, BODY, GRAY); o.append(svg)
    yy += 18
    yy = heading(x, yy, "Rule 1 · its own token")
    o.append(icon("managed-identity", x, yy - 6, 36))
    svg, yy = para(x + 48, yy + 6, "A user-assigned managed identity gets a token for api://stay-api. The spec forbids forwarding the client's token; this trigger never even hands one over.", inner - 48, BODY, GRAY); o.append(svg)
    yy += 18
    yy = heading(x, yy, "Provenance (added)", BLUE)
    o.append(icon("key", x, yy - 6, 34))
    svg, yy = para(x + 48, yy + 6, "Verifies the quote signature against the API's published key before calling book_stay. Fails closed. Key cache five minutes.", inner - 48, BODY, BLUE); o.append(svg)
    o.append(pill(x, top + hgt - 50, "no business rules live here", GREEN, GREEN_BG))

    # API column
    x = M + 2 * (wid + GAP) + PAD
    yy = top + 100
    yy = heading(x, yy, "Rule 1 · auth, roles, logic stay here")
    o.append(icon("entra-id", x, yy - 6, 36))
    svg, yy = para(x + 48, yy + 6, "Built-in authentication validates issuer, audience, signature, expiry. The app reads roles from X-MS-CLIENT-PRINCIPAL.", inner - 48, BODY, GRAY); o.append(svg)
    yy += 18
    yy = heading(x, yy, "Authority (added)", BLUE)
    svg, yy = para(x, yy, "Stays.Read, Bookings.Create, Bookings.Cancel as Entra app roles on the MCP identity. Fresh check on every call. Access is not authority: a reachable tool still gets 403.", inner, BODY, BLUE); o.append(svg)
    yy += 18
    yy = heading(x, yy, "Recovery + audit (added)", BLUE)
    o.append(icon("table-storage", x, yy - 6, 36))
    svg, yy = para(x + 48, yy + 6, "Idempotency-Key → same booking on retry, never a second charge. Audit rows: requested, denied or rejected, authorized, attempted, completed, replayed.", inner - 48, BODY, BLUE); o.append(svg)
    o.append(pill(x, top + hgt - 50, "signature verified again here", GREEN, GREEN_BG))

    # arrows
    ay = top + hgt / 2
    o.append(arrow_labelled(M + wid + 8, M + wid + GAP - 8, ay, "tools/call"))
    o.append(arrow_labelled(M + 2 * wid + GAP + 8, M + 2 * (wid + GAP) - 8, ay, "own bearer"))

    # bottom: the flow with checks
    y = top + hgt + 50
    o.append(text(M, y, "One booking, every check it passes", 17, "700"))
    steps = [
        ("quote_stay", ["API signs the quote", "(price, dates, expiry)"], GREEN, GREEN_BG),
        ("book_stay", ["MCP: confirm = true?", "signature valid?"], AMBER, AMBER_BG),
        ("POST /bookings", ["API: role? signature?", "key seen before?"], AMBER, AMBER_BG),
        ("201 / 200 replay", ["booking written once", "audit: completed"], GREEN, GREEN_BG),
        ("tool result", ["prose the model can act on", "forbidden · not verified · ok"], GREEN, GREEN_BG),
    ]
    n = len(steps)
    sgap = 40
    sw_ = (W - 2 * M - (n - 1) * sgap) / n
    x = M
    for i, (t, lines, fg, bg) in enumerate(steps):
        o.append(box(x, y + 20, sw_, 96, bg, "#E5E7EB", 10))
        o.append(text(x + 14, y + 46, t, 14, "700", fg, mono=True))
        for j, line in enumerate(lines):
            o.append(text(x + 14, y + 70 + j * 19, line, 12.5, "normal", INK))
        if i < n - 1:
            o.append(arrow(x + sw_ + 6, y + 68, x + sw_ + sgap - 6, y + 68))
        x += sw_ + sgap

    o.append(multiline(M, y + 150, "What the model door adds that the REST door does not: a quote that was altered between quote_stay and book_stay is refused by the MCP server before the API is called, and refused again by the API if it ever gets through. What it does not add: any authority. A tool the agent can reach is still a request the API may deny.", W - 2 * M, 13.5, GRAY))

    o.append(text(W - M, H - 14, "sjwiggers.com · Cloud Perspectives · mcp-layer-rules-azure", 11, "normal", LINE, "end"))
    o.append("</svg>")
    return "\n".join(o)


def main() -> None:
    out = {
        "mcp-vs-api-azure": diagram_mcp_vs_api(),
        "mcp-layer-four-rules": diagram_four_rules(),
    }
    for name, svg in out.items():
        (HERE / f"{name}.svg").write_text(svg, encoding="utf-8")
        try:
            import cairosvg  # type: ignore
            cairosvg.svg2png(bytestring=svg.encode("utf-8"), write_to=str(HERE / f"{name}.png"), output_width=2800)
            print(f"wrote {name}.svg and {name}.png")
        except ImportError:
            print(f"wrote {name}.svg (install cairosvg for PNG)")


if __name__ == "__main__":
    main()
