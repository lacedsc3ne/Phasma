#!/usr/bin/env python3
"""
Finds settings that the UI writes but nothing ever reads ("dead toggles").

For every `public X Name { get; set; }` in Models/Persistable/Settings.cs it counts references
to `Prop.Name` in two buckets:
  * UI  - UI\\ViewModels, UI\\Elements\\Settings (pages + their code-behind)
  * app - everything else (Bootstrapper, Watcher, Integrations, Networking, Utility, ...)

A setting with UI references but zero app references is a dead toggle: the page saves it, nothing
acts on it. A setting with zero references anywhere is unused and can be deleted.

Exit code 1 when dead toggles exist (so it can gate a build):
    python tools/check_dead_settings.py
    dotnet build -p:RunSettingsAudit=true        (wired up in PhasmaStrap.csproj)

Known false positives can be listed in tools/dead_settings_allowlist.txt, one name per line.
"""
from __future__ import annotations

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJ = os.path.join(ROOT, "PhasmaStrap")
SETTINGS = os.path.join(PROJ, "Models", "Persistable", "Settings.cs")
ALLOWLIST = os.path.join(os.path.dirname(os.path.abspath(__file__)), "dead_settings_allowlist.txt")

# "UI" = the layer that merely edits a setting: view models plus the pages' XAML. Page
# code-behind, MainWindow and everything outside UI count as consumers.
UI_PREFIXES = (
    os.path.join(PROJ, "UI", "ViewModels"),
)
UI_XAML_DIR = os.path.join(PROJ, "UI", "Elements", "Settings", "Pages")
SKIP = {SETTINGS}


def read(path: str) -> str:
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def setting_names() -> list[str]:
    names = []
    for m in re.finditer(r"^\s*public\s+[\w<>,\[\]\?\s]+?\s+(\w+)\s*\{\s*get;\s*set;", read(SETTINGS), re.M):
        names.append(m.group(1))
    return names


def source_files():
    for base, dirs, files in os.walk(PROJ):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
        for f in files:
            if f.endswith((".cs", ".xaml")) and not f.endswith(".g.cs") and f != "Strings.Designer.cs":
                p = os.path.join(base, f)
                if p not in SKIP:
                    yield p


def main() -> int:
    allow = set()
    if os.path.exists(ALLOWLIST):
        allow = {l.strip() for l in read(ALLOWLIST).splitlines() if l.strip() and not l.startswith("#")}

    names = setting_names()
    ui_hits = {n: 0 for n in names}
    app_hits = {n: 0 for n in names}
    # any `.Name` member access counts - settings are often read through locals like `prop.X`
    patterns = {n: re.compile(r"\." + re.escape(n) + r"\b") for n in names}

    for path in source_files():
        text = read(path)
        is_ui = path.startswith(UI_PREFIXES) or (path.startswith(UI_XAML_DIR) and path.endswith(".xaml"))
        for n, pat in patterns.items():
            c = len(pat.findall(text))
            if not c:
                continue
            if is_ui:
                ui_hits[n] += c
            else:
                app_hits[n] += c

    dead = [n for n in names if ui_hits[n] > 0 and app_hits[n] == 0 and n not in allow]
    unused = [n for n in names if ui_hits[n] == 0 and app_hits[n] == 0 and n not in allow]

    print(f"{len(names)} settings checked")
    if dead:
        print(f"\nDEAD TOGGLES ({len(dead)}) - written by the UI, read by nothing:")
        for n in dead:
            print(f"  - {n}")
    if unused:
        print(f"\nUNUSED ({len(unused)}) - no references anywhere (safe to delete):")
        for n in unused:
            print(f"  - {n}")
    if not dead and not unused:
        print("No dead or unused settings.")

    return 1 if dead else 0


if __name__ == "__main__":
    sys.exit(main())
