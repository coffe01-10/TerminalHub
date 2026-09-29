"""Generate the README's brand scene and palette studies (not UI screenshots).

Run from the repository root: python docs/readme/generate_showcase.py
Uses the existing logo's paths and only Python's standard library.
"""
from html import escape
from pathlib import Path
import generate_logo

ROOT = Path(__file__).resolve().parent


def svg(width, height, title, content):
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="{escape(title)}">
<title>{escape(title)}</title>
{content}
</svg>
'''


def text(x, y, value, size=20, color="#DCE8F7", extra=""):
    return f'<text x="{x}" y="{y}" fill="{color}" font-family="Segoe UI, Inter, Arial, Microsoft YaHei, sans-serif" font-size="{size}" {extra}>{escape(value)}</text>'


def window(x, y, width, height, angle, accent, label):
    lines = []
    for i, length in enumerate((.48, .7, .36, .61)):
        lines.append(f'<rect x="22" y="{70+i*19}" width="{int(width*length)}" height="5" rx="2.5" fill="{accent}" opacity="{.6-i*.11}"/>')
    return f'''<g transform="translate({x} {y}) rotate({angle} {width/2} {height/2})">
<rect x="0" y="10" width="{width}" height="{height}" rx="16" fill="#030914" opacity=".65"/>
<rect width="{width}" height="{height}" rx="16" fill="url(#glass)" stroke="{accent}" stroke-opacity=".48"/>
<path d="M1 39H{width-1}" stroke="{accent}" stroke-opacity=".2"/>
<circle cx="19" cy="20" r="3" fill="{accent}"/>
<circle cx="30" cy="20" r="3" fill="{accent}" opacity=".4"/>
<circle cx="41" cy="20" r="3" fill="{accent}" opacity=".2"/>
{text(width-20,24,label,10,accent,'text-anchor="end" letter-spacing="2"')}
{''.join(lines)}
<path d="M23 {height-34}l9 7-9 7m19 0h17" fill="none" stroke="{accent}" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
</g>'''


def hero(zh=False):
    # Reuse the established three-window emblem and pixel wordmark.
    logo = generate_logo.build()
    logo = logo[logo.index('<defs>'):logo.rindex('</svg>')]
    logo = logo.replace('class="logo-ink"', 'color="#EAF3FF"').replace('class="logo-accent"', 'color="#4AE1CD"')
    tagline = "多个终端，一个工作空间。" if zh else "Every terminal. One workspace."
    subline = "实时预览 / 并排分屏 / 随心切换" if zh else "Live previews / Split panes / Your theme"
    body = '''<defs>
<linearGradient id="back" x2="1" y2="1"><stop stop-color="#08101D"/><stop offset="1" stop-color="#122B43"/></linearGradient>
<linearGradient id="glass" x2=".4" y2="1"><stop stop-color="#203B54"/><stop offset="1" stop-color="#101E31"/></linearGradient>
<radialGradient id="glow"><stop stop-color="#298FAD" stop-opacity=".24"/><stop offset="1" stop-color="#298FAD" stop-opacity="0"/></radialGradient>
<pattern id="grid" width="32" height="32" patternUnits="userSpaceOnUse"><path d="M32 0H0V32" fill="none" stroke="#88BFDA" stroke-opacity=".045"/></pattern>
<clipPath id="frame"><rect width="1200" height="420" rx="24"/></clipPath>
</defs>
<g clip-path="url(#frame)">
<rect width="1200" height="420" fill="url(#back)"/>
<rect width="1200" height="420" fill="url(#grid)"/>
<ellipse cx="963" cy="202" rx="440" ry="370" fill="url(#glow)"/>
<path d="M643 420C751 321 675 189 852 92S1137 73 1240-38" fill="none" stroke="#6FB4D3" stroke-opacity=".16"/>
<path d="M695 420C802 321 740 202 910 125S1170 107 1270 10" fill="none" stroke="#6FB4D3" stroke-opacity=".09"/>
<path d="M60 60h28" stroke="#42D8C1" stroke-width="3"/>
'''
    body += text(102,65,"NATIVE TERMINAL WORKSPACE",13,"#9DB2C9",'letter-spacing="3"')
    body += f'<g transform="translate(54 112) scale(.88)">{logo}</g>'
    body += text(63,274,tagline,31 if zh else 32,"#E8F1FD",'font-weight="600"')
    body += text(64,314,subline,18,"#92A9C2")
    body += '<circle cx="68" cy="367" r="3" fill="#4AE1CD"/>'
    body += text(82,372,"WINDOWS FIRST   ·   LINUX READY   ·   OPEN SOURCE",11,"#91A8C0",'letter-spacing="1.1"')
    body += window(791,51,270,195,-10,"#719DD0","SESSION 01")
    body += window(897,110,248,209,9,"#68A7E8","SESSION 02")
    body += window(741,170,302,209,-3,"#4AE1CD","IN FOCUS")
    body += '<path d="M1142 343h16m-8-8v16" stroke="#4AE1CD" opacity=".55"/><circle cx="738" cy="101" r="3" fill="#E8C989" opacity=".75"/>'
    body += '</g><rect x=".5" y=".5" width="1199" height="419" rx="24" fill="none" stroke="#476780" stroke-opacity=".5"/>'
    return svg(1200,420,"Terminal Hub — " + tagline,body)


THEMES = (
    ("glass", "01", "DARK GLASS", "Depth & light", "#0B111E", "#152436", "#52657B", "#E2E8F0", "#65ACED", "#34D399"),
    ("black", "02", "BLACK", "Less, with focus", "#08090B", "#101114", "#303238", "#E8E9ED", "#A9C8F5", "#67C69C"),
    ("white", "03", "WHITE", "Room to breathe", "#E9EEF4", "#FFFFFF", "#CCD6E2", "#1E2D41", "#245AB5", "#19734B"),
    ("paper", "04", "PAPER", "A warmer workspace", "#E7E0D1", "#F8F3E8", "#C8BDA9", "#3C352B", "#986040", "#526844"),
)


def palette(number, name, subtitle, canvas, surface, border, ink, accent, good):
    body = f'<rect x="1" y="1" width="578" height="183" rx="14" fill="{canvas}" stroke="{border}"/>'
    body += text(26,36,number,12,accent,'letter-spacing="2"')
    body += text(26,77,name,27,ink,'font-weight="700" letter-spacing="1.5"')
    body += text(26,104,subtitle,15,ink,'opacity=".7"')
    # Abstract view motif, deliberately not presented as a rendered UI.
    body += f'<rect x="345" y="27" width="207" height="131" rx="9" fill="{surface}" stroke="{border}"/>'
    body += f'<path d="M345 51h207m-153 0v107" stroke="{border}"/>'
    for y in (64,92,120):
        body += f'<rect x="356" y="{y}" width="32" height="20" rx="3" fill="{canvas}" stroke="{border}"/>'
    body += f'<circle cx="360" cy="39" r="3" fill="{good}"/><path d="M418 74l9 7-9 7m19 0h13" fill="none" stroke="{accent}" stroke-width="3"/>'
    body += f'<path d="M418 103h102m-102 14h73m-73 14h90" stroke="{ink}" stroke-opacity=".15" stroke-width="4"/>'
    for i,color in enumerate((canvas,surface,border,accent,good)):
        body += f'<rect x="{26+i*40}" y="132" width="32" height="22" rx="5" fill="{color}" stroke="{border}"/>'
    return svg(580,185,f"{name} — {subtitle}. Theme palette illustration.",body)


if __name__ == "__main__":
    for language in ("en", "zh"):
        (ROOT / f"hero-{language}.svg").write_text(hero(language == "zh"),encoding="utf-8")
    for key,*theme in THEMES:
        (ROOT / f"theme-{key}.svg").write_text(palette(*theme),encoding="utf-8")
