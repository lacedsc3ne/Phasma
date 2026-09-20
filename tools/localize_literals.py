from __future__ import annotations

import html
import os
import re
import sys
from xml.sax.saxutils import escape as xml_escape

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJ = os.path.join(ROOT, "PhasmaStrap")
PAGES = os.path.join(PROJ, "UI", "Elements", "Settings", "Pages")
EXTRA = [os.path.join(PROJ, "UI", "Elements", "Settings", "MainWindow.xaml")]
RESX = os.path.join(PROJ, "Resources", "Strings.resx")
DESIGNER = os.path.join(PROJ, "Resources", "Strings.Designer.cs")

ATTRS = ("Header", "Description", "Text", "Content", "PlaceholderText", "ToolTip", "Title", "Message")
ATTR_RE = re.compile(r'\s(' + "|".join(ATTRS) + r')="([^"{}]*)"')
RESOURCES_NS = 'xmlns:resources="clr-namespace:PhasmaStrap.Resources"'

def read(p): return open(p, encoding="utf-8-sig").read()
def write(p, s): open(p, "w", encoding="utf-8", newline="").write(s)

def worth_localizing(value: str) -> bool:
    v = html.unescape(value).strip()
    if len(v) < 2:
        return False
    if not re.search(r"[A-Za-z]{2,}", v):
        return False
    if re.fullmatch(r"[A-Za-z0-9_.\-]+\.(png|jpg|xaml|json|exe|dll)", v):
        return False
    if v.startswith("http"):
        return False
    return True

def slug(text: str) -> str:
    words = re.findall(r"[A-Za-z0-9]+", html.unescape(text))
    words = [w for w in words if w.lower() not in ("the", "a", "an", "of", "to", "and", "or", "for", "in", "on", "is", "it", "your", "this", "that", "with")] or words
    s = "".join(w[:1].upper() + w[1:] for w in words[:6])
    if not s:
        s = "Text"
    if s[0].isdigit():
        s = "N" + s
    return s[:60]

def existing_resx() -> tuple[dict[str, str], set[str]]:
    text = read(RESX)
    by_value: dict[str, str] = {}
    keys: set[str] = set()
    for m in re.finditer(r'<data name="([^"]+)" xml:space="preserve">\s*<value>(.*?)</value>', text, re.S):
        key, value = m.group(1), html.unescape(m.group(2))
        keys.add(key)
        by_value.setdefault(value.strip(), key)
    return by_value, keys

def main() -> int:
    dry = "--list" in sys.argv
    by_value, keys = existing_resx()
    designer_props = set(re.findall(r"public static string (\w+) \{", read(DESIGNER)))

    files = sorted(os.path.join(PAGES, f) for f in os.listdir(PAGES) if f.endswith(".xaml")) + EXTRA
    new_entries: list[tuple[str, str]] = []
    total_replaced = 0
    per_file: list[tuple[str, int]] = []

    for path in files:
        xaml = read(path)
        root_class = re.search(r'x:Class="[\w.]+\.(\w+)"', xaml)
        page = (root_class.group(1) if root_class else os.path.splitext(os.path.basename(path))[0]).replace("Page", "") or "Window"
        root_end = xaml.find(">", xaml.find("<ui:UiPage") if "<ui:UiPage" in xaml else xaml.find("<"))
        head, body = xaml[: root_end + 1], xaml[root_end + 1:]

        replaced = 0

        def sub(m: re.Match) -> str:
            nonlocal replaced
            attr, raw = m.group(1), m.group(2)
            if not worth_localizing(raw):
                return m.group(0)
            text = html.unescape(raw)
            key = by_value.get(text.strip())
            if key is None:
                base = f"Menu.{page}.{slug(text)}"
                key = base
                n = 2
                while key in keys or key.replace(".", "_") in designer_props:
                    key = f"{base}{n}"
                    n += 1
                keys.add(key)
                designer_props.add(key.replace(".", "_"))
                by_value[text.strip()] = key
                new_entries.append((key, text))
            replaced += 1
            return f' {attr}="{{x:Static resources:Strings.{key.replace(".", "_")}}}"'

        body2 = ATTR_RE.sub(sub, body)
        if replaced:
            if RESOURCES_NS not in head:
                head = head.replace('xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"', 'xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"\n      ' + RESOURCES_NS, 1)
            per_file.append((os.path.basename(path), replaced))
            total_replaced += replaced
            if not dry:
                write(path, head + body2)

    for name, n in per_file:
        print(f"{name:32} {n:4}")
    print(f"\n{total_replaced} literal attribute(s) across {len(per_file)} file(s); {len(new_entries)} new resx entries, {total_replaced - len(new_entries)} reuse existing ones")

    if dry or not new_entries:
        return 0

    resx = read(RESX)
    additions = "".join(f'  <data name="{k}" xml:space="preserve">\n    <value>{xml_escape(t)}</value>\n  </data>\n' for k, t in new_entries)
    idx = resx.rfind("</root>")
    write(RESX, resx[:idx] + additions + resx[idx:])

    designer = read(DESIGNER)
    props = ""
    for k, t in new_entries:
        props += (
            "        \n"
            f"        public static string {k.replace('.', '_')} {{\n"
            "            get {\n"
            f"                return ResourceManager.GetString(\"{k}\", resourceCulture);\n"
            "            }\n"
            "        }\n"
        )
    idx = designer.rstrip().rfind("    }\n}")
    if idx < 0:
        idx = designer.rstrip().rfind("    }\r\n}")
    write(DESIGNER, designer[:idx] + props + designer[idx:])
    print("resx + Designer.cs updated")
    return 0

if __name__ == "__main__":
    sys.exit(main())
