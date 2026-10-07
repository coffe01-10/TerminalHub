"""Generate the English offline manual from README.en.md.

Install docs/plugins/requirements.txt, then run this script from any directory.
The checked-in HTML is bundled with the app; Markdown is a docs-only dependency.
"""
from html import escape, unescape
from pathlib import Path
import re
from urllib.parse import urlsplit

import markdown

DIRECTORY = Path(__file__).resolve().parent
ROOT = DIRECTORY.parent.parent
REPOSITORY = "https://github.com/coffe01-10/TerminalHub"
OFFLINE = {
    "README.en.md": "index.en.html",
    "README.md": "index.html",
    "development-tutorial.en.md": "development-tutorial.en.html",
    "development-tutorial.md": "development-tutorial.html",
}


def manual_link(match):
    value = unescape(match.group(1))
    url = urlsplit(value)
    if url.scheme or not url.path:
        return match.group(0)
    if url.path in OFFLINE:
        target = OFFLINE[url.path]
    else:
        path = (DIRECTORY / url.path).resolve()
        kind = "tree" if path.is_dir() else "blob"
        target = f"{REPOSITORY}/{kind}/main/{path.relative_to(ROOT).as_posix()}"
    if url.fragment:
        target += "#" + url.fragment
    return f'href="{escape(target, quote=True)}"'


def build():
    source = (DIRECTORY / "README.en.md").read_text(encoding="utf-8")
    # The surrounding page supplies the title and language switch.
    source = source.split("\n", 1)[1].replace("**English** · [简体中文](README.md)", "", 1)
    body = markdown.markdown(source, extensions=["tables", "fenced_code", "toc"])
    body = re.sub(r'href="([^"]+)"', manual_link, body)
    # Preserve the entry anchors used by the app and the Chinese manual.
    for original, replacement in [("install-official-plugins", "install"),
                                   ("official-plugins", "official"),
                                   ("develop-your-own-plugin", "develop"),
                                   ("build-and-distribute", "distribute")]:
        body = body.replace(f'id="{original}"', f'id="{replacement}"')
        body = body.replace(f'href="#{original}"', f'href="#{replacement}"')
    template = (DIRECTORY / "index.html").read_text(encoding="utf-8")
    css = template.split("<style>", 1)[1].split("</style>", 1)[0]
    css = css.replace('"Microsoft YaHei UI","PingFang SC",sans-serif',
                      '"Segoe UI","Microsoft YaHei UI",sans-serif')
    return f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Plugin manual · Terminal Hub</title><style>{css}</style></head>
<body><div class="shell"><aside>
<div class="brand">&gt;_ Terminal Hub</div>
<div class="edition">Plugin development and usage / API 1</div>
<p><a href="index.html" lang="zh-CN">简体中文</a> · English</p>
<nav aria-label="Manual contents">
<a href="#install">01 / Installation</a>
<a href="#official">02 / Official plugins</a>
<a href="#develop">03 / Development</a>
<a href="#distribute">04 / Distribution</a></nav>
<div class="rule"></div><p class="tiny">.NET 8 · Avalonia 11.3.2<br>Windows / Linux<br>This page works offline.</p>
<a class="tiny" href="{REPOSITORY}">Project home ↗</a></aside>
<main><header><div class="eyebrow">WORKBENCH / EXTENSIONS</div>
<h1>Make the workbench<br>work your way.</h1>
<p class="lead">Choose useful tools, arrange where they open, or build your own native plugin with the public SDK.</p></header>
{body}
<footer>Generated from README.en.md · Terminal Hub · MIT · No external fonts, scripts, or telemetry. Source and API links require internet access.</footer>
</main></div></body></html>
'''


if __name__ == "__main__":
    (DIRECTORY / "index.en.html").write_text(build(), encoding="utf-8")
