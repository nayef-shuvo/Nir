# Nir for VS Code

Syntax highlighting for `.nir` files (a TextMate grammar; no language server yet).

## Try it

Open this folder in VS Code and press F5: a second window starts with the extension loaded. Open any file from `samples/`.
"Developer: Inspect Editor Tokens and Scopes" shows the scope of the token under the cursor.

## Install it

```bash
mkdir -p ~/.vscode/extensions/nir-0.1.0
cp -r package.json language-configuration.json syntaxes ~/.vscode/extensions/nir-0.1.0/
```

Restart VS Code. (Or build a `.vsix` with `npx @vscode/vsce package` and run `code --install-extension nir-0.1.0.vsix`.)

## Keeping it in step with the language

The keyword list, the escapes and the string forms come from `src/Nir.Compiler/Parsing/NirLexer.g4`. When a keyword or token is added
there, add it to `syntaxes/nir.tmLanguage.json` too.
