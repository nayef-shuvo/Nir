# Nir logo

**The mark** is a capital **N** made of stepping stones: separate flat circles of different sizes, placed along the three strokes of an N and
growing as they go, with one stone in a different colour. The idea is a project built one small step at a time. Flat solid colours only, no gradients.

The primary colour is golden yellow `#FFC72B` (255, 199, 43). The mark in use now is **07** (`nir-mark-stones-rising.svg`: the README, the extension icon). All three candidates are kept, in case that changes.

| File | Candidate |
|---|---|
| `nir-mark-stones.svg` | **05**: ink stones on a round yellow tile, the top-right stone navy |
| `nir-mark-stones-navy.svg` | **06**: cream and yellow stones on a navy rounded square |
| `nir-mark-stones-rising.svg` | **07**: like 05 but the diagonal rises from bottom left to top right |
| `nir-mark-*-512.png` | renders of each |

The stones need about 32 px to read; at 16 px (a file icon) they blur into a dotted block. That is the thing to judge when choosing.

## Trying a candidate as the `.nir` file icon in your editor

```bash
python3 editors/vscode-nir/tools/add-file-icon.py --list
python3 editors/vscode-nir/tools/add-file-icon.py --icon stones --auto     # or: navy, rising
```

Then, in the editor: "Preferences: File Icon Theme", pick another theme and then your own again (a plain "Reload Window" keeps VS Code's stored copy of the theme). See `editors/vscode-nir/README.md`.

## Palette

| Name | Hex | Where |
|---|---|---|
| **Golden yellow** (primary, the language colour) | `#FFC72B` | the tile of 05 and 07, the yellow stones of 06 |
| Ink | `#2A2623` | the stones of 05 and 07 |
| Navy | `#1B2A49` | the tile of 06, the accent stone of 05 and 07 |
| Cream | `#FFF8E6` | the stones of 06 |

`ai-prompts.md` has the brief, prompts and SVG sources for making more variations with other AIs.
To render a PNG: any SVG renderer, for example `npm i @resvg/resvg-js` and
`new Resvg(svg, { fitTo: { mode: 'width', value: 512 } }).render().asPng()`.
