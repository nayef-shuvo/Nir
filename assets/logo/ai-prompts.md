# Using other AIs for the Nir logo

The brief and ready-to-paste prompts, plus the SVG source of the current candidates so another AI can start from them. The working palette: **primary golden yellow
`#FFC72B`** (RGB 255, 199, 43), ink `#2A2623`, cream `#FFF8E6`, navy `#1B2A49`. Nothing here is final.

## The brief (give this to any AI first)

> **Nir** is a small programming language that runs on .NET ("Kotlin for C#"). I need a logo mark. Personality: calm, elegant, friendly, a little witty.
> Style: flat, geometric, minimal; solid colours only (no gradients, shadows, outlines, glows or textures); no words in the mark, only the shape of a capital **N**
> itself. It must stay readable at 16x16 pixels (editor file icon) as far as possible and look good large. Square 1:1, centred, generous margin, round or rounded-square tile.
> Palette: primary golden yellow #FFC72B; near-black ink #2A2623; warm white #FFF8E6; navy #1B2A49. Use the yellow as the dominant colour.

## The N of stepping stones

A capital N made of separate flat circles ("stones") of different sizes along its three strokes (a column on the left, a diagonal, a column on the right), growing as they go,
with one stone in an accent colour: the project was built one small step at a time.

**Prompt for image generators (Midjourney, DALL-E, Ideogram, Firefly)**

```
Minimal flat vector logo on a golden yellow (#FFC72B) round tile. A capital letter "N" formed only by separate solid circles of different sizes, like stepping stones:
a column of circles on the left, a diagonal of circles going from the top left down to the bottom right, and a column of circles on the right; the circles are near-black (#2A2623)
and one circle at the top right is navy (#1B2A49). Calm, elegant, balanced, generous margin, flat colours only, no gradient, no shadow, no outline, no text, no 3D.
Vector style, crisp edges, 1:1.
--no gradient, shadow, 3d, text, letters other than N, photo, watermark
```

**Prompt for AIs that write SVG (ChatGPT, Claude, Gemini)**: paste the brief, one or two of the SVGs below, and:

```
Here are SVGs of a logo mark made of circles that form a capital N (viewBox 0 0 256 256). Create 6 new versions that still read clearly as an N: vary the tile
(round, rounded square), the colour roles (which stones get which colour, which one is the accent), the sizes (even, growing, biggest at one end) and the spacing. Only <circle>
elements on a <rect> or <circle> tile; solid fills only (no opacity, gradients or filters); all circles inside the tile with an even margin and no overlaps. Palette from the brief.
Also say which version stays most legible at 16x16 pixels. Output each as its own SVG code block with a short name.
```

## Tips

- Image generators are poor at exact geometry; use them for mood and colour ideas, then rebuild the one you like as SVG (any SVG-writing AI can do that from a description plus the SVG below).
- Always paste the **exact hex codes** and "solid flat colours, no gradient"; say "no text" or they add letters.
- Ask for **several variations at once** and say what to vary.
- Check every result at **16 px** before liking it.
- To use a result: save it as an SVG into `assets/logo/` and ask for it to be wired in (README, editor icon, file icon, colours).

## SVG source of the current candidates (`viewBox="0 0 256 256"`)

### 05: yellow round tile

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">
  <circle cx="128" cy="128" r="128" fill="#FFC72B"/>
  <g fill="#2A2623">
    <circle cx="78" cy="76" r="16"/>
    <circle cx="78" cy="128" r="16"/>
    <circle cx="78" cy="180" r="16"/>
    <circle cx="106" cy="98" r="13"/>
    <circle cx="130" cy="128" r="13"/>
    <circle cx="154" cy="158" r="13"/>
    <circle cx="180" cy="128" r="16"/>
    <circle cx="180" cy="180" r="16"/>
  </g>
  <circle cx="180" cy="76" r="18" fill="#1B2A49"/>
</svg>
```

### 06: navy rounded square

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">
  <rect width="256" height="256" rx="64" fill="#1B2A49"/>
  <g fill="#FFF8E6">
    <circle cx="78" cy="76" r="14"/>
    <circle cx="78" cy="128" r="14"/>
    <circle cx="78" cy="180" r="14"/>
    <circle cx="106" cy="98" r="12"/>
    <circle cx="130" cy="128" r="13"/>
  </g>
  <circle cx="154" cy="158" r="15" fill="#FFC72B"/>
  <g fill="#FFC72B">
    <circle cx="180" cy="76" r="19"/>
    <circle cx="180" cy="128" r="15"/>
    <circle cx="180" cy="180" r="13"/>
  </g>
</svg>
```

### 07: yellow, the diagonal rises

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">
  <circle cx="128" cy="128" r="128" fill="#FFC72B"/>
  <g fill="#2A2623">
    <circle cx="78" cy="76" r="16"/>
    <circle cx="78" cy="128" r="16"/>
    <circle cx="78" cy="180" r="16"/>
    <circle cx="106" cy="158" r="13"/>
    <circle cx="130" cy="128" r="13"/>
    <circle cx="154" cy="98" r="13"/>
    <circle cx="180" cy="128" r="16"/>
    <circle cx="180" cy="180" r="16"/>
  </g>
  <circle cx="180" cy="76" r="18" fill="#1B2A49"/>
</svg>
```
