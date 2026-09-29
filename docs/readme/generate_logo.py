"""Regenerate Terminal Hub's README logo. Python standard library only.

Draws a badge tile with the app's three-windows hub motif plus a pixel
wordmark; ink colors follow the viewer's light/dark preference.
"""
from pathlib import Path

CELL = 7          # pixel cell edge in viewBox units
GLYPH_W = 5       # columns per glyph
GLYPH_H = 7       # rows per glyph
TRACK = 1         # cells between glyphs
WORD_GAP = 3      # cells between words

FONT = {
    "T": ("#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."),
    "E": ("#####", "#....", "#....", "####.", "#....", "#....", "#####"),
    "R": ("####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"),
    "M": ("#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"),
    "I": ("#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"),
    "N": ("#...#", "##..#", "##..#", "#.#.#", "#..##", "#..##", "#...#"),
    "A": (".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"),
    "L": ("#....", "#....", "#....", "#....", "#....", "#....", "#####"),
    "H": ("#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"),
    "U": ("#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."),
    "B": ("####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."),
}

WORD_PARTS = (("TERMINAL", "logo-ink"), ("HUB", "logo-accent"))

# Emblem geometry, in a 100x100 tile drawn at (10, 10).
TILE = 100
TILE_X = 10
TILE_Y = 10


def glyph_rects(letter: str, origin_x: int, origin_y: int) -> str:
    parts = []
    for row, bits in enumerate(FONT[letter]):
        col = 0
        while col < GLYPH_W:
            if bits[col] != "#":
                col += 1
                continue
            run = col
            while col < GLYPH_W and bits[col] == "#":
                col += 1
            x = origin_x + run * CELL
            y = origin_y + row * CELL
            parts.append(f'<rect x="{x}" y="{y}" width="{(col - run) * CELL}" height="{CELL}" fill="currentColor"/>')
    return "".join(parts)


def wordmark(origin_x: int, origin_y: int) -> tuple[str, int]:
    """Draw both word parts; return svg fragments and the x after the last glyph."""
    out = []
    x = origin_x
    for word, cls in WORD_PARTS:
        glyphs = "".join(
            glyph_rects(ch, x + i * (GLYPH_W + TRACK) * CELL, origin_y)
            for i, ch in enumerate(word)
        )
        out.append(f'<g class="{cls}" shape-rendering="crispEdges">{glyphs}</g>')
        x += (len(word) * GLYPH_W + (len(word) - 1) * TRACK + WORD_GAP) * CELL
    return "".join(out), x - (WORD_GAP - TRACK) * CELL


def emblem() -> str:
    w = TILE
    return f'''<g transform="translate({TILE_X},{TILE_Y})">
<rect width="{w}" height="{w}" rx="20" fill="url(#tile)"/>
<g transform="rotate(-13,24,26)"><rect x="6" y="13" width="36" height="26" rx="5" fill="#1E73E3" stroke="#FFFFFF" stroke-width="4.5"/><circle cx="14" cy="20" r="2.2" fill="#FFFFFF"/><circle cx="21" cy="20" r="2.2" fill="#FFFFFF"/><circle cx="28" cy="20" r="2.2" fill="#FFFFFF"/></g>
<g transform="rotate(13,76,26)"><rect x="58" y="13" width="36" height="26" rx="5" fill="#1E73E3" stroke="#FFFFFF" stroke-width="4.5"/><circle cx="66" cy="20" r="2.2" fill="#FFFFFF"/><circle cx="73" cy="20" r="2.2" fill="#FFFFFF"/><circle cx="80" cy="20" r="2.2" fill="#FFFFFF"/></g>
<rect x="26" y="51" width="48" height="36" rx="6" fill="#1E73E3" stroke="#FFFFFF" stroke-width="4.5"/>
<circle cx="36" cy="60" r="2.4" fill="#FFFFFF"/><circle cx="44.5" cy="60" r="2.4" fill="#FFFFFF"/><circle cx="53" cy="60" r="2.4" fill="#FFFFFF"/>
<path d="M35 68L42 72.5L35 77" fill="none" stroke="#FFFFFF" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"/>
<path d="M48 77.5H61" stroke="#FFFFFF" stroke-width="3.5" stroke-linecap="round"/>
<circle cx="50" cy="48" r="10" fill="#FFFFFF"/><circle cx="50" cy="48" r="6.5" fill="#19CFC2"/>
</g>'''


def build() -> str:
    word_x = TILE_X + TILE + 22
    word_y = 34
    mark, cursor_x = wordmark(word_x, word_y)
    cursor_x += TRACK * CELL
    cursor_y = word_y + (GLYPH_H - 1) * CELL
    cursor = (
        f'<g class="logo-accent" shape-rendering="crispEdges">'
        f'<rect x="{cursor_x}" y="{cursor_y}" width="21" height="{CELL}" fill="currentColor"/></g>'
    )
    width = cursor_x + 21 + 8
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="120" viewBox="0 0 {width} 120" role="img" aria-label="Terminal Hub">
<title>Terminal Hub</title>
<desc>三扇终端窗口汇聚成枢纽的徽章与像素字标；墨色随明暗主题变化。</desc>
<style>.logo-ink{{color:#1C2B3C}}.logo-accent{{color:#0E9488}}@media(prefers-color-scheme:dark){{.logo-ink{{color:#E2ECF8}}.logo-accent{{color:#2BD4C6}}}}</style>
<defs><linearGradient id="tile" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2B82F0"/><stop offset="1" stop-color="#1768D9"/></linearGradient></defs>
{emblem()}
{mark}
{cursor}
</svg>
'''


if __name__ == "__main__":
    directory = Path(__file__).resolve().parent
    (directory / "logo.svg").write_text(build(), encoding="utf-8")
