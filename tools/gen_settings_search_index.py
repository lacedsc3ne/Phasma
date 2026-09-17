#!/usr/bin/env python3
"""
Generates PhasmaStrap/UI/Elements/Settings/Search/SettingsSearchIndex.g.cs by scanning every
settings page's XAML for the things a user can search for:

  * controls:OptionControl   (Header / Description)            -> Option
  * ui:CardExpander          (header text + optional subtitle) -> Group
  * TabItem                  (header text)                     -> Tab
  * section-title TextBlocks (FontSize 16-18 / Medium+)        -> Section
  * standalone ui:ToggleSwitch / CheckBox with Content         -> Option
  * standalone ui:Button with literal Content                  -> Action

Pages embedded inside another page's tab via <Frame Source="XPage.xaml"> are attributed to the
host page + that tab, so navigating to a result lands on the page the user can actually click in
the sidebar. Header/description text that comes from {x:Static resources:Strings.X} is emitted as
a reference to that same resource, so the index follows the active translation automatically.

Re-run after changing any page XAML:
    python tools/gen_settings_search_index.py
"""
from __future__ import annotations

import os
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PAGES_DIR = os.path.join(ROOT, "PhasmaStrap", "UI", "Elements", "Settings", "Pages")
MAIN_WINDOW = os.path.join(ROOT, "PhasmaStrap", "UI", "Elements", "Settings", "MainWindow.xaml")
OUTPUT = os.path.join(ROOT, "PhasmaStrap", "UI", "Elements", "Settings", "Search", "SettingsSearchIndex.g.cs")

STATIC_RE = re.compile(r"^\{x:Static\s+resources:Strings\.([A-Za-z0-9_]+)\s*\}$")
SKIP_LOCAL_NAMES = {
    "DataTemplate", "ItemTemplate", "ContextMenu", "ToolTip", "Resources", "Style", "Triggers",
    "ItemsPanelTemplate", "ControlTemplate", "HeaderTemplate", "ItemContainerStyle",
}


def local(tag: str) -> str:
    return tag.split("}", 1)[1] if "}" in tag else tag


def attr(el: ET.Element, name: str) -> str | None:
    for key, value in el.attrib.items():
        if local(key) == name:
            return value
    return None


def to_expr(value: str | None) -> str | None:
    """XAML attribute -> C# expression (Strings.X reference or string literal), or None if dynamic."""
    if value is None:
        return None
    value = value.strip()
    if not value:
        return None
    m = STATIC_RE.match(value)
    if m:
        return f"Strings.{m.group(1)}"
    if value.startswith("{"):
        return None  # Binding / DynamicResource / other markup - can't be indexed statically
    return cs_literal(value)


def cs_literal(text: str) -> str:
    text = text.replace("\\", "\\\\").replace('"', '\\"').replace("\r", "").replace("\n", " ")
    text = re.sub(r"\s+", " ", text).strip()
    return f'"{text}"'


def first_textblock_text(el: ET.Element) -> tuple[str | None, str | None]:
    """Header content element -> (title expr, subtitle expr) from its first/second TextBlocks."""
    texts: list[str] = []
    for node in el.iter():
        if local(node.tag) == "TextBlock":
            expr = to_expr(attr(node, "Text"))
            if expr:
                texts.append(expr)
    title = texts[0] if texts else None
    subtitle = texts[1] if len(texts) > 1 else None
    return title, subtitle


@dataclass
class Entry:
    kind: str
    header: str
    description: str | None
    tab: str | None
    section: str | None
    group: str | None

    def key(self):
        return (self.kind, self.header, self.tab, self.section, self.group)


@dataclass
class PageInfo:
    cls: str
    file: str
    entries: list[Entry] = field(default_factory=list)
    title_seen: bool = False
    # nested page class -> tab expr of the host tab that embeds it
    nested: dict[str, str | None] = field(default_factory=dict)


# generic verbs that appear on dozens of buttons ("Edit", "Browse", "Refresh"...) - indexing them
# would bury real settings under identical-looking rows, and the option/group they belong to is
# already indexed on its own
GENERIC_ACTIONS = {
    "folder", "refresh", "import", "browse", "apply", "cancel", "close", "save", "reset", "preview", "remove",
    "add", "delete", "edit", "rename", "new", "open", "ok", "copy", "clear", "export", "load", "select", "pick",
    "choose", "test", "restart", "help", "back", "next", "done", "retry", "stop", "start", "run", "launch",
}


def is_meaningful_action(expr: str) -> bool:
    if expr.startswith("Strings.Common_"):
        return False
    if not expr.startswith('"'):
        return True
    text = expr.strip('"').strip().lower().rstrip(".…")
    if len(text) < 4:
        return False
    return text not in GENERIC_ACTIONS


def font_size(el: ET.Element) -> float | None:
    v = attr(el, "FontSize")
    try:
        return float(v) if v else None
    except ValueError:
        return None


def is_section_title(el: ET.Element) -> bool:
    size = font_size(el)
    weight = (attr(el, "FontWeight") or "").lower()
    if size is not None and 15 <= size < 20:
        return True
    if size is None and weight in ("medium", "semibold", "bold"):
        return True
    if size is not None and size >= 14 and weight in ("semibold", "bold"):
        return True
    return False


def walk(el: ET.Element, page: PageInfo, tab: str | None, section: str | None, group: str | None,
         in_option: bool, depth: int = 0) -> str | None:
    """Walks children; returns the section title in effect after this element (sections persist
    across siblings until the next one)."""
    for child in list(el):
        name = local(child.tag)

        if name in SKIP_LOCAL_NAMES or name.endswith(".Resources") or name.endswith(".ContextMenu") \
                or name.endswith(".ToolTip") or name.endswith(".ItemTemplate") or name.endswith(".Style") \
                or name.endswith(".ItemContainerStyle") or name.endswith(".Triggers"):
            continue

        if name == "TabItem":
            header_expr = to_expr(attr(child, "Header"))
            header_el = next((c for c in child if local(c.tag) == "TabItem.Header"), None)
            if header_expr is None and header_el is not None:
                header_expr, _ = first_textblock_text(header_el)
            if header_expr:
                page.entries.append(Entry("Tab", header_expr, None, None, None, None))
            # frames embedding another page
            for frame in child.iter():
                if local(frame.tag) == "Frame":
                    src = attr(frame, "Source")
                    if src and src.lower().endswith(".xaml"):
                        page.nested[os.path.splitext(os.path.basename(src))[0]] = header_expr
            walk(child, page, header_expr, None, None, in_option, depth + 1)
            continue

        if name == "TabItem.Header" or name == "CardExpander.Header":
            continue

        if name == "CardExpander":
            header_expr = to_expr(attr(child, "Header"))
            desc_expr = None
            header_el = next((c for c in child if local(c.tag) == "CardExpander.Header"), None)
            if header_expr is None and header_el is not None:
                header_expr, desc_expr = first_textblock_text(header_el)
            if header_expr:
                page.entries.append(Entry("Group", header_expr, desc_expr, tab, section, None))
            walk(child, page, tab, section, header_expr or group, in_option, depth + 1)
            continue

        if name == "OptionControl":
            header_expr = to_expr(attr(child, "Header"))
            desc_expr = to_expr(attr(child, "Description"))
            if header_expr:
                page.entries.append(Entry("Option", header_expr, desc_expr, tab, section, group))
            walk(child, page, tab, section, group, True, depth + 1)
            continue

        if name == "TextBlock" and not in_option:
            text_expr = to_expr(attr(child, "Text"))
            size = font_size(child)
            if text_expr and size is not None and size >= 20 and not page.title_seen and tab is None:
                # the page's own title (first big TextBlock) - not an entry, not a section
                page.title_seen = True
                continue
            if text_expr and (is_section_title(child) or (size is not None and size >= 20)):
                section = text_expr
                page.entries.append(Entry("Section", text_expr, None, tab, None, None))
                continue

        if name in ("ToggleSwitch", "CheckBox") and not in_option:
            content_expr = to_expr(attr(child, "Content"))
            if content_expr:
                page.entries.append(Entry("Option", content_expr, None, tab, section, group))

        if name == "Button" and not in_option:
            content_expr = to_expr(attr(child, "Content"))
            if content_expr and is_meaningful_action(content_expr):
                page.entries.append(Entry("Action", content_expr, None, tab, section, group))

        section = walk(child, page, tab, section, group, in_option, depth + 1) or section

    return section


def parse_page(path: str) -> PageInfo | None:
    tree = ET.parse(path)
    root = tree.getroot()
    cls = attr(root, "Class")
    if not cls:
        return None
    page = PageInfo(cls=cls.split(".")[-1], file=os.path.basename(path))
    walk(root, page, None, None, None, False)
    return page


def parse_nav() -> dict[str, str]:
    """page class -> nav label expr, from MainWindow.xaml's NavigationItems."""
    tree = ET.parse(MAIN_WINDOW)
    labels: dict[str, str] = {}
    for el in tree.getroot().iter():
        if local(el.tag) != "NavigationItem":
            continue
        page_type = attr(el, "PageType") or ""
        m = re.search(r"pages:([A-Za-z0-9_]+)", page_type)
        if not m:
            continue
        content = to_expr(attr(el, "Content"))
        if content:
            labels[m.group(1)] = content
    return labels


def main() -> int:
    nav = parse_nav()
    pages: dict[str, PageInfo] = {}
    for file in sorted(os.listdir(PAGES_DIR)):
        if not file.endswith(".xaml"):
            continue
        info = parse_page(os.path.join(PAGES_DIR, file))
        if info:
            pages[info.cls] = info

    # nested page -> (host class, tab expr)
    host_of: dict[str, tuple[str, str | None]] = {}
    for cls, info in pages.items():
        for nested_cls, tab in info.nested.items():
            host_of[nested_cls] = (cls, tab)

    lines: list[str] = []
    lines.append("// <auto-generated>")
    lines.append("//     Generated by tools/gen_settings_search_index.py from the settings pages' XAML.")
    lines.append("//     Do not edit by hand - re-run the script after changing any page.")
    lines.append("// </auto-generated>")
    lines.append("")
    lines.append("using PhasmaStrap.Resources;")
    lines.append("using PhasmaStrap.UI.Elements.Settings.Pages;")
    lines.append("")
    lines.append("namespace PhasmaStrap.UI.Elements.Settings.Search")
    lines.append("{")
    lines.append("    internal static partial class SettingsSearchIndex")
    lines.append("    {")
    lines.append("        private static List<SettingsSearchEntry> BuildGenerated() => new()")
    lines.append("        {")

    total = 0
    per_page: dict[str, int] = {}
    for cls in sorted(pages):
        info = pages[cls]
        if cls in host_of:
            host_cls, host_tab = host_of[cls]
            nav_label = nav.get(host_cls)
            page_type = host_cls
            nested_type = f"typeof({cls})"
        else:
            host_tab = None
            nav_label = nav.get(cls)
            page_type = cls
            nested_type = "null"
        if nav_label is None:
            print(f"warning: {cls} is neither a nav item nor embedded in one - skipped", file=sys.stderr)
            continue

        seen = set()
        lines.append(f"            // ---- {info.file} ({cls}) ----")
        for e in info.entries:
            # an embedded page keeps its own tab; the host page's tab (the one holding the
            # frame) travels separately so the navigator can open both in order
            key = (e.kind, e.header, host_tab, e.tab, e.section, e.group)
            if key in seen:
                continue
            seen.add(key)
            desc = e.description or '""'
            tab_expr = e.tab or '""'
            host_expr = host_tab or '""'
            section_expr = e.section or '""'
            group_expr = e.group or '""'
            lines.append(
                f"            new(SettingsSearchEntryKind.{e.kind}, {e.header}, {desc}, typeof({page_type}), {nav_label}, {tab_expr}, {section_expr}, {group_expr}, {nested_type}, {host_expr}),")
            total += 1
            per_page[cls] = per_page.get(cls, 0) + 1

    lines.append("        };")
    lines.append("    }")
    lines.append("}")
    lines.append("")

    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    with open(OUTPUT, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("\n".join(lines))

    for cls in sorted(per_page):
        print(f"{cls:28} {per_page[cls]:4}")
    print(f"total entries: {total} -> {os.path.relpath(OUTPUT, ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
