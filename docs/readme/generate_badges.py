"""Regenerate Terminal Hub's local README badges. Python standard library only."""
from html import escape
from pathlib import Path

BADGES = (
    ("runtime", "RUNTIME", ".NET 8", "#65ACED"),
    ("ui", "UI", "AVALONIA 11", "#BCA5F4"),
    ("platform", "DESKTOP", "WIN / LINUX", "#6AD7BD"),
    ("license", "LICENSE", "MIT", "#F2CC86"),
)


def badge(label: str, value: str, accent: str) -> str:
    left = 20 + len(label) * 7
    right = 20 + len(value) * 7
    width = left + right
    title = escape(f"{label}: {value}")
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="26" viewBox="0 0 {width} 26" role="img" aria-label="{title}">
  <title>{title}</title>
  <path d="M4 1H{width-4}L{width-1} 4V22L{width-4} 25H4L1 22V4Z" fill="#142132" stroke="#52657B"/>
  <path d="M{left} 1V25" stroke="#52657B"/>
  <g font-family="Consolas, Menlo, monospace" font-size="11" font-weight="600" text-anchor="middle">
    <text x="{left/2}" y="17" fill="#C5D0DF">{escape(label)}</text>
    <text x="{left+right/2}" y="17" fill="{accent}">{escape(value)}</text>
  </g>
</svg>
'''


if __name__ == "__main__":
    directory = Path(__file__).resolve().parent
    for name, label, value, accent in BADGES:
        (directory / f"{name}.svg").write_text(badge(label, value, accent), encoding="utf-8")
