#!/usr/bin/env python3
"""Put the Nir icon for `.nir` files into the file icon theme you use, so you can try each logo candidate in your own editor.

Why this exists: an extension can offer a file icon for its language, but VS Code shows it only if the active icon theme does not
define icons of its own (most popular icon themes do, so `.nir` files get a plain "file" icon). An extension cannot change another
icon theme, so this script edits the theme's own `theme.json`: it copies the chosen icon next to it and maps the `nir` extension and
language to it. A backup `theme.json.orig` is written once. Running it again with another `--icon` replaces the icon. A theme
update installs a fresh copy of the theme and removes the change; run the script again then.

IMPORTANT, VS Code caches the icon theme: "Developer: Reload Window" keeps using its stored copy of the theme and never reads the
changed `theme.json`. After running this script, open the command palette, run "Preferences: File Icon Theme", pick any other
theme, then pick your own theme again. That makes VS Code read the theme from disk. (Each run also writes the icon under a new
file name, so the editor's image cache cannot show an old icon.)

usage:
  tools/add-file-icon.py --list
  tools/add-file-icon.py [--icon NAME] --auto            patch the icon theme that VS Code and VS Code Insiders are using now
  tools/add-file-icon.py [--icon NAME] THEME_JSON...     patch the given theme.json files

NAME is one of the candidates below (default: rising).
"""
import argparse
import hashlib
import json
import pathlib
import re
import shutil
import sys

REPOSITORY = pathlib.Path(__file__).resolve().parents[3]
LOGOS = REPOSITORY / "assets" / "logo"
ICONS = {
    "stones": ("nir-mark-stones.svg", "05: stones N on a round yellow tile"),
    "navy": ("nir-mark-stones-navy.svg", "06: stones N on a navy rounded square"),
    "rising": ("nir-mark-stones-rising.svg", "07: stones N, yellow, the diagonal rises"),
}
INSTALLED_PREFIX = "nir-file-icon"
KEY = "nir"


def editors():
    """(name, user settings.json, extensions folder) of VS Code and VS Code Insiders on this machine."""
    home = pathlib.Path.home()
    if sys.platform == "darwin":
        config = home / "Library" / "Application Support"
    elif sys.platform == "win32":
        config = pathlib.Path(__import__("os").environ.get("APPDATA", home))
    else:
        config = home / ".config"
    return [
        ("VS Code", config / "Code" / "User" / "settings.json", home / ".vscode" / "extensions"),
        ("VS Code Insiders", config / "Code - Insiders" / "User" / "settings.json", home / ".vscode-insiders" / "extensions"),
    ]


def active_icon_theme(settings: pathlib.Path):
    if not settings.exists():
        return None
    match = re.search(r'"workbench\.iconTheme"\s*:\s*"([^"]+)"', settings.read_text(encoding="utf-8"))
    return match.group(1) if match else None


def find_theme_file(extensions: pathlib.Path, theme_id: str):
    """The theme.json of the newest installed extension that contributes the icon theme with this id."""
    for folder in sorted(extensions.glob("*"), reverse=True):
        manifest = folder / "package.json"
        if not manifest.exists():
            continue
        try:
            contributes = json.loads(manifest.read_text(encoding="utf-8")).get("contributes", {})
        except (OSError, ValueError):
            continue
        for theme in contributes.get("iconThemes", []):
            if theme.get("id") == theme_id:
                return (folder / theme["path"]).resolve()
    return None


def patch(theme_path: pathlib.Path, icon: pathlib.Path, name: str) -> None:
    theme = json.loads(theme_path.read_text(encoding="utf-8"))
    backup = theme_path.with_name(theme_path.name + ".orig")
    if not backup.exists():
        shutil.copyfile(theme_path, backup)

    # A new file name for every icon and every change, so a cached image can never be shown for the new icon.
    digest = hashlib.sha1(icon.read_bytes()).hexdigest()[:8]
    installed = f"{INSTALLED_PREFIX}-{name}-{digest}.svg"
    for old in theme_path.parent.glob(INSTALLED_PREFIX + "*.svg"):
        old.unlink()
    shutil.copyfile(icon, theme_path.with_name(installed))
    theme.setdefault("iconDefinitions", {})[KEY] = {"iconPath": "./" + installed}
    theme.setdefault("fileExtensions", {})[KEY] = KEY
    theme.setdefault("languageIds", {})[KEY] = KEY
    theme_path.write_text(json.dumps(theme, indent=2) + "\n", encoding="utf-8")
    print(f"patched {theme_path}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--icon", choices=sorted(ICONS), default="rising")
    parser.add_argument("--list", action="store_true", help="show the candidate icons")
    parser.add_argument("--auto", action="store_true", help="patch the icon theme each editor uses now")
    parser.add_argument("themes", nargs="*", help="theme.json files to patch")
    arguments = parser.parse_args()

    if arguments.list:
        for name, (file, description) in ICONS.items():
            print(f"{name:8} {description}  ({file})")
        return 0

    icon = LOGOS / ICONS[arguments.icon][0]
    targets = [pathlib.Path(t).expanduser() for t in arguments.themes]
    if arguments.auto:
        for name, settings, extensions in editors():
            theme_id = active_icon_theme(settings)
            if theme_id is None:
                print(f"{name}: no icon theme set in {settings}, skipped (the default theme shows the extension's own icon)")
                continue
            found = find_theme_file(extensions, theme_id)
            if found is None:
                print(f"{name}: icon theme '{theme_id}' not found in {extensions}, skipped")
                continue
            print(f"{name}: icon theme '{theme_id}'")
            targets.append(found)

    if not targets:
        parser.print_usage()
        return 2

    for target in targets:
        patch(target, icon, arguments.icon)
    print(f"icon: {arguments.icon} ({ICONS[arguments.icon][1]}).")
    print("Now, in each editor: command palette > 'Preferences: File Icon Theme' > pick another theme, then pick your own again.")
    print("(Reload Window is not enough: VS Code keeps a stored copy of the icon theme.)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
