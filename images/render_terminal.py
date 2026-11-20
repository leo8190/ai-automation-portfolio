#!/usr/bin/env python3
"""Render demo CLI output as clean 1200px-wide PNG screenshots."""

from __future__ import annotations

import re
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

WIDTH = 1200
SCALE = 2  # render at 2x, then downscale for sharper type
PAD_X = 36
PAD_Y = 22
TITLE_H = 44
FOOTER_H = 36
LINE_GAP = 1.62

FONT_REG = "/usr/share/fonts/truetype/sand-box/google/IBM Plex Mono/IBMPlexMono-Regular.ttf"
FONT_MED = "/usr/share/fonts/truetype/sand-box/google/IBM Plex Mono/IBMPlexMono-Medium.ttf"
FONT_BOLD = "/usr/share/fonts/truetype/sand-box/google/IBM Plex Mono/IBMPlexMono-Bold.ttf"

# Dark, high-contrast terminal
BG = (15, 18, 24)
TITLE_BG = (28, 33, 43)
BORDER = (42, 48, 61)
RULE = (48, 55, 70)
TEXT = (220, 227, 235)
DIM = (132, 143, 158)
MUTED = (108, 119, 135)
HEAD = (125, 207, 232)
STEP = (159, 196, 232)
GREEN = (86, 201, 148)
AMBER = (230, 188, 86)
RED = (232, 120, 126)
PURPLE = (196, 167, 232)
CYAN = (125, 207, 232)
WHITE = (236, 240, 245)
DOTS = [(255, 95, 86), (255, 189, 46), (39, 201, 63)]

STATUS_COLORS = {
    "ReadyToQuote": GREEN,
    "NeedsInfo": AMBER,
    "UnknownService": RED,
    "Approved": GREEN,
    "paid=True": GREEN,
    "Needs-Human": RED,
    "sales_lead": CYAN,
    "partnership": PURPLE,
    "support": AMBER,
    "billing": GREEN,
    "scheduling": CYAN,
    "spam": MUTED,
    "complaint": RED,
    "Won": GREEN,
    "Lost": MUTED,
    "New": CYAN,
    "Contacted": STEP,
    "Proposal": PURPLE,
    "Upcoming": AMBER,
}

TOKEN_RE = re.compile(
    r"(ReadyToQuote|NeedsInfo|UnknownService|Needs-Human|paid=True|"
    r"sales_lead|partnership|support|billing|scheduling|spam|complaint|"
    r"Upcoming|Approved all|"
    r"\$\s*\d{1,3}(?:,\d{3})*(?:\.\d{2})?|"
    r"\b(?:REQ|Q|INV|LEAD|MSG|M|T)-\d[\w-]*\b|"
    r"\b(?:New|Contacted|Proposal|Won|Lost)\b)"
)

EMOJI = {
    "\U0001F7E1": AMBER,  # yellow circle
    "\U0001F534": RED,  # red circle
    "\u26AA": (190, 198, 208),  # white circle
}


def load_font(path: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(path, size)


def sanitize(text: str) -> str:
    # Drop machine-specific absolute paths so portfolio shots stay portable.
    text = re.sub(r"\S+?/(out/)", r"\1", text)
    return text.replace("\r\n", "\n").replace("\r", "\n")


def measure_char_w(font: ImageFont.FreeTypeFont) -> int:
    # IBM Plex Mono is monospaced; measure a typical glyph.
    bbox = font.getbbox("M")
    return bbox[2] - bbox[0]


def choose_font_size(lines: list[str], max_content_w: int) -> int:
    # Fit the longest visible line (emoji count as two cells) into the width.
    def cells(line: str) -> int:
        n = 0
        for ch in line:
            if ch in EMOJI or ch == "\u26A0":
                n += 2
            else:
                n += 1
        return n

    longest = max((cells(l) for l in lines), default=1)
    for size in range(15, 11, -1):
        font = load_font(FONT_REG, size * SCALE)
        if measure_char_w(font) * longest <= max_content_w:
            return size
    return 12


def visible_chunks(line: str) -> list[tuple[str, str]]:
    """Split a line into ('text', s) / ('dot', color_key) / ('warn', '') chunks."""
    chunks: list[tuple[str, str]] = []
    buf = []
    i = 0
    while i < len(line):
        ch = line[i]
        if ch in EMOJI:
            if buf:
                chunks.append(("text", "".join(buf)))
                buf = []
            chunks.append(("dot", ch))
            i += 1
            continue
        if ch == "\u26A0":
            if buf:
                chunks.append(("text", "".join(buf)))
                buf = []
            chunks.append(("warn", ch))
            i += 1
            # skip optional variation selector
            if i < len(line) and line[i] == "\uFE0F":
                i += 1
            continue
        buf.append(ch)
        i += 1
    if buf:
        chunks.append(("text", "".join(buf)))
    return chunks


def line_style(line: str) -> tuple[str, tuple[int, int, int]]:
    stripped = line.strip()
    if stripped.startswith("==="):
        return "bold", HEAD
    if stripped.startswith("Step "):
        return "medium", STEP
    if stripped.startswith("All ") and "fictional" in stripped:
        return "regular", DIM
    if stripped.startswith("Done."):
        return "regular", DIM
    if "Approved all" in stripped:
        return "regular", TEXT
    return "regular", TEXT


def draw_title_bar(
    draw: ImageDraw.ImageDraw,
    w: int,
    title_h: int,
    command: str,
    font: ImageFont.FreeTypeFont,
) -> None:
    draw.rectangle([0, 0, w, title_h], fill=TITLE_BG)
    r = int(6.5 * SCALE)
    cy = title_h // 2
    x = int(16 * SCALE)
    gap = int(16 * SCALE)
    for color in DOTS:
        draw.ellipse([x - r, cy - r, x + r, cy + r], fill=color)
        x += gap
    # Command label, vertically centered
    bbox = font.getbbox(command)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    tx = (w - tw) // 2
    ty = (title_h - th) // 2 - bbox[1]
    draw.text((tx, ty), command, font=font, fill=(176, 186, 200))
    # Bottom rule
    draw.line([(0, title_h - 1), (w, title_h - 1)], fill=RULE, width=SCALE)


def highlight_text(
    draw: ImageDraw.ImageDraw,
    x: int,
    y: int,
    text: str,
    font: ImageFont.FreeTypeFont,
    default: tuple[int, int, int],
    char_w: int,
    font_reg: ImageFont.FreeTypeFont,
    font_med: ImageFont.FreeTypeFont,
) -> None:
    # Draw with token colors. Use a single baseline so mixed fonts line up.
    pos = 0
    last = 0
    for m in TOKEN_RE.finditer(text):
        if m.start() > last:
            piece = text[last : m.start()]
            draw.text((x + pos * char_w, y), piece, font=font, fill=default)
            pos += len(piece)
        token = m.group(0)
        if token.startswith("$"):
            color = GREEN
        elif token.startswith("Approved"):
            color = GREEN
        elif re.match(r"^(REQ|Q|INV|LEAD|MSG|M|T)-", token):
            color = CYAN
        else:
            color = STATUS_COLORS.get(token, default)
        use = font_med if color != default else font
        draw.text((x + pos * char_w, y), token, font=use, fill=color)
        pos += len(token)
        last = m.end()
    if last < len(text):
        piece = text[last:]
        draw.text((x + pos * char_w, y), piece, font=font, fill=default)


def draw_dot(
    draw: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    radius: int,
    color: tuple[int, int, int],
) -> None:
    draw.ellipse([cx - radius, cy - radius, cx + radius, cy + radius], fill=color)
    # subtle rim
    rim = tuple(min(255, c + 30) for c in color)
    draw.ellipse(
        [cx - radius, cy - radius, cx + radius, cy + radius],
        outline=rim,
        width=max(1, SCALE),
    )


def draw_warn(
    draw: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    size: int,
) -> None:
    h = size
    w = int(size * 1.15)
    pts = [
        (cx, cy - h // 2),
        (cx - w // 2, cy + h // 2),
        (cx + w // 2, cy + h // 2),
    ]
    draw.polygon(pts, fill=AMBER)
    # bang
    font = load_font(FONT_BOLD, max(9 * SCALE // 1, 10))
    bang = "!"
    bbox = font.getbbox(bang)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    draw.text((cx - tw / 2, cy - th / 2 - bbox[1] + 1 * SCALE), bang, font=font, fill=BG)


def render(output_path: Path, raw: str, command: str, footer: str) -> None:
    text = sanitize(raw).rstrip("\n") + "\n"
    lines = text.split("\n")
    # Keep a trailing blank line out of the body; footer covers the close.
    if lines and lines[-1] == "":
        lines = lines[:-1]

    content_w = (WIDTH - 2 * PAD_X) * SCALE
    font_size = choose_font_size(lines, content_w)
    font = load_font(FONT_REG, font_size * SCALE)
    font_med = load_font(FONT_MED, font_size * SCALE)
    font_bold = load_font(FONT_BOLD, font_size * SCALE)
    font_title = load_font(FONT_MED, 12 * SCALE)
    font_foot = load_font(FONT_REG, 11 * SCALE)

    char_w = measure_char_w(font)
    line_h = int(font_size * SCALE * LINE_GAP)
    # Use font metrics for a stable baseline
    ascent, descent = font.getmetrics()
    # Center glyphs in the line box
    glyph_h = ascent + descent

    pad_x = PAD_X * SCALE
    pad_y = PAD_Y * SCALE
    title_h = TITLE_H * SCALE
    footer_h = FOOTER_H * SCALE
    body_h = pad_y + line_h * len(lines) + pad_y
    img_w = WIDTH * SCALE
    img_h = title_h + body_h + footer_h

    img = Image.new("RGB", (img_w, img_h), BG)
    draw = ImageDraw.Draw(img)

    # Outer border
    draw.rectangle([0, 0, img_w - 1, img_h - 1], outline=BORDER, width=SCALE)

    draw_title_bar(draw, img_w, title_h, command, font_title)

    y = title_h + pad_y
    for line in lines:
        style, default_color = line_style(line)
        face = {"regular": font, "medium": font_med, "bold": font_bold}[style]
        # Vertical offset so the glyph sits in the line box
        text_y = y + (line_h - glyph_h) // 2
        baseline_cy = text_y + ascent
        x = pad_x
        chunks = visible_chunks(line)
        # If the whole line is plain text (most lines), use highlighter.
        if len(chunks) == 1 and chunks[0][0] == "text":
            highlight_text(
                draw, x, text_y, chunks[0][1], face, default_color, char_w, font, font_med
            )
        else:
            for kind, payload in chunks:
                if kind == "text":
                    highlight_text(
                        draw, x, text_y, payload, face, default_color, char_w, font, font_med
                    )
                    x += len(payload) * char_w
                elif kind == "dot":
                    # Two-cell slot, circle centered in the first cell-and-a-half
                    slot = 2 * char_w
                    cx = x + slot // 2
                    radius = max(5 * SCALE, int(font_size * SCALE * 0.38))
                    draw_dot(draw, cx, baseline_cy - ascent // 3, radius, EMOJI[payload])
                    x += slot
                elif kind == "warn":
                    slot = 2 * char_w
                    cx = x + slot // 2
                    draw_warn(draw, cx, baseline_cy - ascent // 3, int(font_size * SCALE * 0.95))
                    x += slot
        y += line_h

    # Footer
    fy0 = title_h + body_h
    draw.line([(0, fy0), (img_w, fy0)], fill=RULE, width=SCALE)
    draw.rectangle([0, fy0, img_w, img_h], fill=TITLE_BG)
    bbox = font_foot.getbbox(footer)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    tx = pad_x
    ty = fy0 + (footer_h - th) // 2 - bbox[1]
    draw.text((tx, ty), footer, font=font_foot, fill=MUTED)
    # Redo outer border on top
    draw.rectangle([0, 0, img_w - 1, img_h - 1], outline=BORDER, width=SCALE)

    out = img.resize((WIDTH, img_h // SCALE), Image.Resampling.LANCZOS)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    out.save(output_path, "PNG", optimize=True)
    print(f"wrote {output_path} ({out.size[0]}x{out.size[1]})")


def main() -> None:
    root = Path(__file__).resolve().parent
    runs = Path("/tmp/demo-runs")
    footer = "Sample data is fictional. Nothing was sent."
    jobs = [
        (
            "quote-invoice.png",
            "quote-invoice.txt",
            "dotnet run --project src/QuoteInvoice -- demo",
        ),
        (
            "crm-followup.png",
            "crm-followup.txt",
            "dotnet run --project src/CrmFollowup -- demo",
        ),
        (
            "email-agent.png",
            "email-agent.txt",
            "dotnet run --project src/EmailAgent --no-launch-profile -- demo",
        ),
    ]
    for name, src, command in jobs:
        raw = (runs / src).read_text(encoding="utf-8")
        render(root / name, raw, command, footer)


if __name__ == "__main__":
    main()
