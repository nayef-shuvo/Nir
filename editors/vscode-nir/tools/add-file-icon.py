#!/usr/bin/env python3
"""Add the Nir icon for `.nir` files to a file icon theme that is installed in VS Code.

Why this exists: an extension can offer a file icon for its language, but VS Code shows it only if the active icon theme does not
define icons of its own (most popular icon themes do, so `.nir` files get their plain "file" icon). Icon themes cannot be extended
by other extensions, so this script edits the theme's own `theme.json`: it copies `images/file-icon.svg` next to it and maps the
`nir` extension and language to it. A backup `theme.json.orig` is written once. Running it again is harmless.
A theme update installs a fresh copy of the theme and removes the change; run the script again then.

usage: tools/add-file-icon.py <path to the theme.json of the icon theme> [more theme.json paths...]
Find the path with: grep -l '"iconDefinitions"' ~/.vscode/extensions/<icon theme folder>/**/*.json
"""
import json
import pathlib
import shutil
import sys

ICON = pathlib.Path(__file__).resolve().parent.parent / "images" / "file-icon.svg"
NAME = "nir-file-icon.svg"
KEY = "nir"


def patch(theme_path: pathlib.Path) -> None:
    theme = json.loads(theme_path.read_text(encoding="utf-8"))
    backup = theme_path.with_name(theme_path.name + ".orig")
    if not backup.exists():
        shutil.copyfile(theme_path, backup)

    shutil.copyfile(ICON, theme_path.with_name(NAME))
    theme.setdefault("iconDefinitions", {})[KEY] = {"iconPath": "./" + NAME}
    theme.setdefault("fileExtensions", {})[KEY] = KEY
    theme.setdefault("languageIds", {})[KEY] = KEY
    theme_path.write_text(json.dumps(theme, indent=2) + "\n", encoding="utf-8")
    print(f"patched {theme_path}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    for argument in sys.argv[1:]:
        patch(pathlib.Path(argument).expanduser())
