"""Generate stable README illustrations, independent of application page layouts.

Run from the repository root: python docs/readme/generate_showcase.py
Only Python's standard library is required.
"""
from html import escape
from pathlib import Path

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


def emblem(x, y, scale=1):
    """Reuse the project's three-terminal motif, without third-party CLI logos."""
    return f'''<g transform="translate({x} {y}) scale({scale})">
<rect width="100" height="100" rx="22" fill="#2476E9"/>
<g fill="#2476E9" stroke="#F5FAFF" stroke-width="4" stroke-linejoin="round">
<rect x="9" y="17" width="34" height="27" rx="5" transform="rotate(-12 26 30)"/>
<rect x="57" y="17" width="34" height="27" rx="5" transform="rotate(12 74 30)"/>
<rect x="25" y="51" width="50" height="35" rx="6"/>
</g>
<path d="M35 63L43 69L35 75M49 76H62" fill="none" stroke="#F5FAFF" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
<path d="M37 44L44 51M63 44L56 51" stroke="#F5FAFF" stroke-width="3"/>
</g>'''


def hero(zh=False):
    title = "Terminal Hub — 专为 AI CLI 打造的终端管理工作台" if zh else "Terminal Hub — A workbench built for AI CLIs"
    description = ("Claude Code、Codex CLI 和 Shell 会话围绕 Terminal Hub 汇聚。"
                   "这是一幅产品定位概念图，不是界面截图。" if zh else
                   "Claude Code, Codex CLI, and shell sessions surround Terminal Hub. "
                   "A conceptual product illustration, not an application screenshot.")
    body = '''<defs>
<linearGradient id="canvas" x2="1" y2="1"><stop stop-color="#0B1422"/><stop offset="1" stop-color="#142C43"/></linearGradient>
<radialGradient id="halo"><stop stop-color="#388CCC" stop-opacity=".22"/><stop offset="1" stop-color="#388CCC" stop-opacity="0"/></radialGradient>
<pattern id="grid" width="36" height="36" patternUnits="userSpaceOnUse"><path d="M36 0H0V36" fill="none" stroke="#9EC8E9" stroke-opacity=".045"/></pattern>
<clipPath id="frame"><rect width="1200" height="500" rx="24"/></clipPath>
</defs>
<g clip-path="url(#frame)">
<rect width="1200" height="500" fill="url(#canvas)"/>
<rect width="1200" height="500" fill="url(#grid)"/>
<ellipse cx="934" cy="251" rx="342" ry="330" fill="url(#halo)"/>
<path d="M665 64V436" stroke="#92B2CE" stroke-opacity=".16"/>
<path d="M66 63H94" stroke="#4DE0C7" stroke-width="3"/>
'''
    body += text(108, 68, "BUILT AROUND YOUR AI CLI", 12, "#A9BED5", 'letter-spacing="2"')
    body += text(63, 173, "Terminal Hub", 68, "#F1F6FC", 'font-weight="700" letter-spacing="-2"')
    if zh:
        body += text(66, 245, "专为 AI CLI 打造的", 27, "#B7CBDD")
        body += text(63, 300, "终端管理工作台", 44, "#F1F6FC", 'font-weight="600"')
    else:
        body += text(65, 247, "Built for AI CLIs.", 40, "#F1F6FC", 'font-weight="600"')
        body += text(66, 297, "Keep your sessions together.", 27, "#B7CBDD")
    body += '<path d="M66 359H595" stroke="#9EBAD2" stroke-opacity=".2"/>'
    body += text(66, 399, "LOCAL SHELLS   /   REAL PTYs   /   OPEN SOURCE", 12, "#B7CBDD", 'letter-spacing="1"')
    body += text(66, 440, "把注意力留给任务。" if zh else "Keep your attention on the work.", 17, "#8DA8C2")
    # Session nodes and connections express containment, not AI orchestration.
    body += '''<g fill="none" stroke="#78A9CC" stroke-width="1.5">
<circle cx="924" cy="252" r="150" stroke-opacity=".19"/>
<circle cx="924" cy="252" r="100" stroke-dasharray="3 9" stroke-opacity=".3"/>
<path d="M840 130C896 130 870 223 924 223" stroke="#F0C9A5"/>
<path d="M1016 234H986" stroke="#80BDF0"/>
<path d="M848 372C917 372 898 294 924 294" stroke="#4DE0C7"/>
</g>
<circle cx="924" cy="252" r="68" fill="#182F47" stroke="#3D6686"/>
'''
    body += emblem(874, 202)
    nodes = [(710, 102, 170, "$ claude", "#F0C9A5"),
             (1000, 206, 166, "$ codex", "#80BDF0"),
             (728, 344, 152, "$ shell", "#4DE0C7")]
    for x, y, width, label, color in nodes:
        body += f'<rect x="{x}" y="{y}" width="{width}" height="56" rx="12" fill="#101F31" stroke="{color}" stroke-opacity=".65"/>'
        body += text(x + 20, y + 35, label, 22, color, mono=True)
    body += text(925, 442, "MANY SESSIONS. ONE WORKBENCH.", 12, "#ACC3D8", 'text-anchor="middle" letter-spacing="1.6"')
    body += '</g><rect x=".5" y=".5" width="1199" height="499" rx="24" fill="none" stroke="#4C6D8B" stroke-opacity=".6"/>'
    return svg(1200, 500, title, description, body)


def compact_hero(zh=False):
    title = "Terminal Hub — 专为 AI CLI 打造的终端管理工作台" if zh else "Terminal Hub — A workbench built for AI CLIs"
    body = '''<defs>
<linearGradient id="canvas" x2="1" y2="1"><stop stop-color="#0B1422"/><stop offset="1" stop-color="#142C43"/></linearGradient>
</defs>
<rect x=".5" y=".5" width="599" height="379" rx="22" fill="url(#canvas)" stroke="#4C6D8B"/>
<path d="M34 37H55" stroke="#4DE0C7" stroke-width="3"/>
'''
    body += text(69, 41, "BUILT AROUND YOUR AI CLI", 11, "#A9BED5", 'letter-spacing="1.5"')
    body += text(30, 111, "Terminal Hub", 64, "#F1F6FC", 'font-weight="700" letter-spacing="-2"')
    if zh:
        body += text(34, 165, "专为 AI CLI 打造的", 24, "#B7CBDD")
        body += text(32, 213, "终端管理工作台", 36, "#F1F6FC", 'font-weight="600"')
    else:
        body += text(33, 169, "Built for AI CLIs.", 34, "#F1F6FC", 'font-weight="600"')
        body += text(34, 212, "Your sessions, together.", 24, "#B7CBDD")
    body += emblem(461, 139, .78)
    for x, label, color in [(34, "$ claude", "#F0C9A5"), (216, "$ codex", "#80BDF0"), (398, "$ shell", "#4DE0C7")]:
        body += f'<rect x="{x}" y="259" width="168" height="54" rx="10" fill="#101F31" stroke="{color}" stroke-opacity=".65"/>'
        body += text(x + 18, 293, label, 21, color, mono=True)
    body += text(300, 352, "MANY SESSIONS. ONE WORKBENCH.", 13, "#ACC3D8", 'text-anchor="middle" letter-spacing="1.6"')
    return svg(600, 380, title, "Compact product illustration for narrow README layouts, not an application screenshot.", body)


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
