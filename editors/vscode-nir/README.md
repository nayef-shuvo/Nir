# Nir for VS Code

Syntax highlighting for `.nir` files (a TextMate grammar; no language server yet).

## Install it (VS Code and VS Code Insiders)

Build a `.vsix` and let the editor install it. Copying the folder into `~/.vscode/extensions` does **not** work: VS Code only loads
extensions listed in `extensions.json` there, and Insiders uses its own folder (`~/.vscode-insiders/extensions`).

```bash
cd editors/vscode-nir
npx @vscode/vsce package --allow-missing-repository --skip-license
code --install-extension nir-0.1.5.vsix
code-insiders --install-extension nir-0.1.5.vsix
```

Reload the window (or restart the editor), open a `.nir` file, and check that the status bar says "Nir". When you change the
grammar, raise `version` in `package.json` and install again.

## Try it without installing

Open this folder in VS Code and press F5: a second window starts with the extension loaded and opens `samples/`.
"Developer: Inspect Editor Tokens and Scopes" shows the scope of the token under the cursor.

## Keeping it in step with the language

The keyword list, the escapes and the string forms come from `src/Nir.Compiler/Parsing/NirLexer.g4`. When a keyword or token is added
there, add it to `syntaxes/nir.tmLanguage.json` too. The icon is rendered from `assets/logo/nir-mark.svg`.

## The file icon

`package.json` offers `images/file-icon.svg` as the icon of `.nir` files. VS Code shows it only when the active **file icon theme** has no
icons of its own (the default Seti theme, for example). Popular icon themes (Catppuccin, Material, vscode-icons, ...) define their own
icons and show a plain file icon for `.nir`, and an extension cannot change a theme. For such a theme, add the icon to the theme itself:

```bash
python3 tools/add-file-icon.py <the theme.json of the icon theme>
```

It copies the icon next to the theme's `theme.json`, maps `.nir` to it and keeps a `theme.json.orig` backup. A theme update replaces its files,
so run the script again after updating the theme. Reload the window afterwards.
