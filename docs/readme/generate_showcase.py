"""Generate README artwork matching the Terminal Hub launch film.

Run from the repository root: python docs/readme/generate_showcase.py
Only Python's standard library is required.
"""
from html import escape
from pathlib import Path

from generate_logo import emblem as brand_emblem, wordmark

ROOT = Path(__file__).resolve().parent
FONT = "Segoe UI, Inter, Arial, Microsoft YaHei, sans-serif"
MONO = "Cascadia Mono, Consolas, DejaVu Sans Mono, monospace"


def text(x, y, value, size=20, color="#E8F0FA", extra="", mono=False):
    font = MONO if mono else FONT
    return (f'<text x="{x}" y="{y}" fill="{color}" font-family="{font}" '
            f'font-size="{size}" {extra}>{escape(value)}</text>')


def svg(width, height, title, description, content):
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}"
     viewBox="0 0 {width} {height}" role="img" aria-label="{escape(title)}">
<title>{escape(title)}</title>
<desc>{escape(description)}</desc>
{content}
</svg>
'''


def hero(zh=False, compact=False):
    width, height = (600, 330) if compact else (1200, 390)
    center = width // 2
    title = "Terminal Hub — 让 AI 写代码，你掌控全局。" if zh else "Terminal Hub — Let AI code. Stay in control."
    body = f'''<defs>
<linearGradient id="canvas" x2="1" y2="1"><stop stop-color="#070C13"/><stop offset="1" stop-color="#102238"/></linearGradient>
<radialGradient id="halo"><stop stop-color="#65ACED" stop-opacity=".18"/><stop offset="1" stop-color="#65ACED" stop-opacity="0"/></radialGradient>
<linearGradient id="tile" x2="0" y2="1"><stop stop-color="#2B82F0"/><stop offset="1" stop-color="#1768D9"/></linearGradient>
<linearGradient id="line"><stop stop-color="#65ACED"/><stop offset="1" stop-color="#2BD4C6"/></linearGradient>
</defs>
<rect x="1" y="1" width="{width - 2}" height="{height - 2}" rx="22" fill="url(#canvas)" stroke="#21354D"/>
<ellipse cx="{center}" cy="150" rx="{center - 15}" ry="150" fill="url(#halo)"/>
<path d="M{center - 25} 42H{center + 25}" stroke="url(#line)" stroke-width="3"/>
'''
    scale = .8 if compact else 1.35
    x = (width - 644 * scale) / 2
    y = 73 if compact else 80
    mark, _ = wordmark(132, 34)
    mark = mark.replace('class="logo-accent"', 'class="logo-accent" style="color:#2BD4C6"')
    body += f'''<g transform="translate({x} {y}) scale({scale})">
{brand_emblem()}
<g style="color:#E2ECF8">{mark}</g>
<rect x="615" y="76" width="21" height="7" fill="#2BD4C6"/>
</g>'''
    tagline = "让 AI 写代码，你掌控全局。" if zh else "Let AI code. Stay in control."
    sub = "专为 AI CLI 打造的原生终端工作台" if zh else "A native terminal workbench for AI CLIs."
    body += text(center, 215 if compact else 266, tagline, 28 if compact else 38,
                 extra='text-anchor="middle" font-weight="600"')
    body += text(center, 257 if compact else 311, sub, 18 if compact else 23,
                 "#A8B9CC", 'text-anchor="middle"')
    body += text(center, 298 if compact else 358, "CLAUDE CODE  /  CODEX CLI  /  YOUR SHELL", 12 if compact else 14,
                 "#65ACED", 'text-anchor="middle" letter-spacing="1"', mono=True)
    return svg(width, height, title,
               "Launch-film colors and the project's pixel wordmark. Brand artwork, not an application screenshot.", body)


def compact_hero(zh=False):
    return hero(zh, compact=True)


def workflow():
    body = '''<rect x="1" y="1" width="1198" height="278" rx="20" fill="#F3F6FA" stroke="#CDD8E5"/>
<path d="M403 52V228M797 52V228" stroke="#D3DDE8"/>
'''
    steps = [(30, "01", "START", "Project + shell", "#2476E9"),
             (425, "02", "WORK", "Independent sessions", "#168875"),
             (820, "03", "RETURN", "Restore your layout", "#8C5132")]
    for x, number, label, caption, color in steps:
        body += text(x + 24, 47, number, 12, color, 'font-weight="600" letter-spacing="2"')
        body += text(x + 24, 182, label, 29, "#213348", 'font-weight="700" letter-spacing="1.2"')
        body += text(x + 24, 217, caption, 19, "#526278")
    # Folder: a project, rather than a particular application's page.
    body += '''<g transform="translate(54 73)" fill="none" stroke="#2476E9" stroke-width="3" stroke-linejoin="round">
<path d="M0 12V56H74V15H35L27 5H0V12Z"/><path d="M13 30H58M13 42H45" stroke-opacity=".6"/>
</g>
<g transform="translate(449 73)" fill="none" stroke="#168875" stroke-width="2.5" stroke-linejoin="round">
<rect width="45" height="42" rx="5"/><rect x="58" width="45" height="42" rx="5"/><rect x="116" width="45" height="42" rx="5"/>
<path d="M11 12L20 21L11 30M23 30H33M69 12L78 21L69 30M81 30H91M127 12L136 21L127 30M139 30H149"/>
</g>
<g transform="translate(844 72)" fill="none" stroke="#8C5132" stroke-width="3" stroke-linejoin="round">
<path d="M56 5A31 31 0 1 0 63 49M56 5H40M56 5V21" stroke-linecap="round"/>
<rect x="17" y="22" width="27" height="25" rx="3"/><path d="M17 32H44M28 32V47"/>
</g>
<path d="M344 187H366L359 180M366 187L359 194M738 187H760L753 180M760 187L753 194" fill="none" stroke="#8297AE" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
'''
    return svg(1200, 280, "Start a project. Work across sessions. Restore your layout.",
               "A workflow illustration, not an application screenshot. Restoring a layout creates fresh shell processes.",
               body)


if __name__ == "__main__":
    for language in ("en", "zh"):
        (ROOT / f"hero-{language}.svg").write_text(hero(language == "zh"), encoding="utf-8")
        (ROOT / f"hero-{language}-compact.svg").write_text(compact_hero(language == "zh"), encoding="utf-8")
    (ROOT / "workflow.svg").write_text(workflow(), encoding="utf-8")
