# Nir logo

**The mark** is a lowercase *n* built from two halves that meet at the top: a thin one (salmon) and a thick one (cream), on a plum
tile. The two halves are the language and the platform it stands on (.NET) joining in the middle; they are not equal, and they fit.
Flat shapes only, no gradients, so it stays clear down to 16 px.

**The second mark** (`nir-mark-stones.svg`) is a capital *N* made of seven stepping stones that grow as they go and end in one big
mauve stone: the project was built one small step at a time. It is for larger places (banners, a website); at 16 px the stones turn
into dots.

| File | Use |
|---|---|
| `nir-mark.svg` | The main mark (source). The editor and file icon; works from 16 px up. |
| `nir-mark-stones.svg` | The stepping-stones mark (source), on a round salmon tile. |
| `nir-logo.svg`, `nir-logo-dark.svg` | The main mark with the word "nir": dark letters for light pages, light letters for dark pages. The word is drawn as paths, so it needs no font. |
| `nir-mark-512.png`, `nir-mark-stones-512.png`, `nir-logo-1280.png` | Renders. |

## Palette

The language colour is salmon. The colours come from a five-stripe palette picked by the author, plus a plum and a rose.

| Name | Hex | Where |
|---|---|---|
| **Salmon** (the language colour) | `#F5A07E` | the thin half, the stones tile, the dot of the `i` |
| Plum | `#55384A` | the tile of the main mark, the letters of the word |
| Cream | `#FFF4EA` | the thick half, the stones |
| Mauve | `#AC6E81` | the big stone |
| Rose | `#D78A9C` | spare accent |
| Mint, teal, mustard | `#A8D3AB`, `#84C5CB`, `#E2AA35` | spare accents from the same palette |

To render the PNGs again, use any SVG renderer, for example `npm i @resvg/resvg-js` and
`new Resvg(svg, { fitTo: { mode: 'width', value: 512 } }).render().asPng()`.
