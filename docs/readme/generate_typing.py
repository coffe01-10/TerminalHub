"""Regenerate the README typing animation. Python standard library only.

A small terminal window that types the build-and-run commands character by
character, using the SMIL textPath-growth technique popularized by
readme-typing-svg (github.com/DenverCoder1/readme-typing-svg). Rendered
locally with the project palette instead of calling the hosted service.
"""
from html import escape
from pathlib import Path

LINES = (
    "pwsh",
    "dotnet restore",
    "dotnet build TerminalHub.sln",
    "dotnet run --project src/TerminalHub.App",
)

PROMPT = "> "
FONT_SIZE = 17
ADV = 10                # px advance per character (enforced by textLength)
PAD_X = 18              # left margin of typed text
TITLE_H = 30            # title-bar divider height
LINE_H = 26             # baseline spacing
FIRST_Y = 52            # first baseline
BOTTOM_PAD = 20
RIGHT_PAD = 18

# Timing (ms)
LEAD = 400              # empty prompt before the first line starts
CHAR = 78               # per character
PAUSE = 450             # between commands
HOLD = 1600             # keep the finished transcript visible
BLINK = 420             # cursor idle half-period

COLORS = {
    "window": "#101C2B",
    "frame": "#2A4059",
    "divider": "#22374E",
    "title": "#4A6A88",
    "text": "#C5D0DF",
    "accent": "#2BD4C6",
    "dots": ("#65ACED", "#6AD7BD", "#F2CC86"),
}


def timeline() -> tuple[list[tuple[int, int, int]], int]:
    """Return [(start, end, chars) per line] and the cycle length T."""
    t = LEAD
    spans = []
    for i, line in enumerate(LINES):
        start = t
        end = start + (len(PROMPT) + len(line)) * CHAR
        spans.append((start, end, len(PROMPT) + len(line)))
        t = end + (HOLD if i == len(LINES) - 1 else PAUSE)
    return spans, t


def line_animation(index: int, start: int, total: int, chars: int) -> str:
    dur = total - start
    times = [(k - 1) * CHAR for k in range(1, chars + 1)]
    times.append(dur)
    values = [f"m0,{FIRST_Y + index * LINE_H} h{k * ADV}" for k in range(1, chars + 1)]
    values.append(values[-1])
    keytimes = ";".join(f"{t / dur:.4f}" for t in times)
    vals = " ; ".join(values)
    begin = f"{start}ms;c0.end+{start}ms;c1.end+{start}ms"
    return (
        f'<path id="p{index}">'
        f'<animate id="d{index}" attributeName="d" dur="{dur}ms" begin="{begin}" '
        f'fill="remove" calcMode="discrete" values="{vals}" keyTimes="{keytimes}"/>'
        f"</path>"
    )


def cursor_states(spans: list[tuple[int, int, int]], total: int):
    """Sample cursor x/y/opacity at every event boundary of the cycle."""
    times = {0}
    for i, (start, end, chars) in enumerate(spans):
        for k in range(chars + 1):
            times.add(start + k * CHAR)
        nxt = spans[i + 1][0] if i + 1 < len(spans) else total
        t = end
        while t < nxt:
            times.add(t)
            t += BLINK
        times.add(nxt - 1)  # settle visible just before the next segment
    ordered = sorted(t for t in times if t < total)

    states = []
    for t in ordered:
        x, y, on = PAD_X, 0, 1
        placed = False
        for i, (start, end, chars) in enumerate(spans):
            if start <= t < end:
                k = (t - start) // CHAR + 1
                x, y, placed = PAD_X + int(k) * ADV, i, True
                break
            if end <= t:
                nxt_start = spans[i + 1][0] if i + 1 < len(spans) else total
                if t < nxt_start:
                    nxt_line = i + 1 if i + 1 < len(spans) else i
                    x, y, placed = PAD_X, nxt_line, True
                    on = 0 if int((t - end) // BLINK) % 2 else 1
                    if i + 1 == len(spans):  # hold: cursor stays at line end
                        x = PAD_X + chars * ADV
                        y = i
                    break
        if not placed:  # LEAD segment: blinking empty prompt
            x, y = PAD_X, 0
            on = 0 if int(t // BLINK) % 2 else 1
        states.append((x, FIRST_Y + y * LINE_H - FONT_SIZE + 2, on))
    return ordered, states


def build() -> str:
    spans, total = timeline()
    max_chars = max(len(PROMPT) + len(line) for line in LINES)
    width = PAD_X + max_chars * ADV + RIGHT_PAD
    height = FIRST_Y + (len(LINES) - 1) * LINE_H + BOTTOM_PAD

    paths = "".join(
        line_animation(i, start, total, chars)
        for i, (start, end, chars) in enumerate(spans)
    )
    texts = []
    for i, line in enumerate(LINES):
        length = (len(PROMPT) + len(line)) * ADV
        texts.append(
            f'<text font-family="Consolas, Menlo, monospace" font-size="{FONT_SIZE}" '
            f'xml:space="preserve"><textPath xlink:href="#p{i}" startOffset="0" '
            f'textLength="{length}">'
            f'<tspan fill="{COLORS["accent"]}">{escape(PROMPT)}</tspan>'
            f'<tspan fill="{COLORS["text"]}">{escape(line)}</tspan>'
            f"</textPath></text>"
        )

    times, states = cursor_states(spans, total)
    keytimes = ";".join(f"{t / total:.4f}" for t in times) + ";1.0000"
    xs = ";".join(str(s[0]) for s in states) + f";{states[-1][0]}"
    ys = ";".join(str(s[1]) for s in states) + f";{states[-1][1]}"
    ops = ";".join(str(s[2]) for s in states) + f";{states[-1][2]}"

    dots = "".join(
        f'<circle cx="{22 + i * 16}" cy="{TITLE_H // 2}" r="4" fill="{c}"/>'
        for i, c in enumerate(COLORS["dots"])
    )
    cx_mid = width / 2

    return f'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="Terminal typing the build and run commands">
<title>pwsh — dotnet run --project src/TerminalHub.App</title>
<desc>动画演示在终端里依次输入还原、构建和启动命令；循环播放，不是录屏。</desc>
<rect width="{width}" height="{height}" rx="10" fill="{COLORS["window"]}" stroke="{COLORS["frame"]}" stroke-width="1.5"/>
{dots}
<text x="{cx_mid}" y="{TITLE_H // 2 + 4}" font-family="Consolas, Menlo, monospace" font-size="12" fill="{COLORS["title"]}" text-anchor="middle">pwsh</text>
<path d="M0 {TITLE_H}H{width}" stroke="{COLORS["divider"]}" stroke-width="1.5"/>
{paths}
{"".join(texts)}
<rect width="{ADV}" height="{FONT_SIZE + 3}" fill="{COLORS["accent"]}">
<animate attributeName="x" dur="{total}ms" begin="0s;c0.end;c1.end" fill="remove" calcMode="discrete" values="{xs}" keyTimes="{keytimes}"/>
<animate attributeName="y" dur="{total}ms" begin="0s;c0.end;c1.end" fill="remove" calcMode="discrete" values="{ys}" keyTimes="{keytimes}"/>
<animate attributeName="opacity" dur="{total}ms" begin="0s;c0.end;c1.end" fill="remove" calcMode="discrete" values="{ops}" keyTimes="{keytimes}"/>
</rect>
<rect width="0" height="0" opacity="0">
<animate id="c0" attributeName="x" values="0;1" dur="{total}ms" begin="0s;c1.end"/>
<animate id="c1" attributeName="x" values="0;1" dur="{total}ms" begin="c0.end"/>
</rect>
</svg>
'''


if __name__ == "__main__":
    directory = Path(__file__).resolve().parent
    (directory / "typing.svg").write_text(build(), encoding="utf-8")
