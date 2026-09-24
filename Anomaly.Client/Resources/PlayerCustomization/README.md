# Player Customization Resources

Convert neutralized HDRP base-map PNGs offline with `tools/Convert-NeutralMap.ps1`. Place the resulting `.rgba32` resource in this folder. It is embedded into
`Anomaly.Client.dll` at build time (via the `<EmbeddedResource Include="..." />`
glob in `Anomaly.Client.csproj`) and loaded by `PlayerColorTracker` and other
customization features as `_BaseColorMap` overrides paired with a
`MaterialPropertyBlock` `_BaseColor` tint.

## Currently expected files

### `ClassD_jumpsuit_BaseMap_neutral.rgba32` (Class D jumpsuit color customization)

A luminance-preserving, chroma-flattened version of the stock SCP:SL Class D
jumpsuit base map.

- **Source**: `Class-D Personnel_ClassD Jumpsuit Material_BaseMap.png` from the
  AssetRipper export of the SCP:SL client.
- **Process**: desaturate so all RGB channels hold the source's luminance
  (Rec.601: `0.299*R + 0.587*G + 0.114*B`) while preserving alpha. The dark
  detail (skeleton overlays, "D SITE-02" stencil) must still read as dark on
  the output. The orange tint must be gone.
- **Why**: HDRP_Lit samples `_BaseColorMap * _BaseColor`. The stock map is
  orange (~RGB 230,130,50), so a `_BaseColor` multiply alone with that map
  cannot produce cool colors — picking blue against orange gives black. With
  this neutralized map, multiplying by any user-chosen color faithfully
  reproduces that color while keeping the texture's contrast.

If this file is missing, `PlayerColorTracker` logs a warning at startup and
falls back to `_BaseColor`-only tinting (warm-palette tints look correct,
cool-palette tints look dark/black).

The neutralized PNG is intentionally **not** committed to the repo because it
is derived from Northwood's SCP:SL texture asset; each developer (or the
in-house build pipeline) should bake their own copy from a local SCP:SL
install before producing a release build.

The binary format is an 8-byte ASCII ARGBA32 plus NUL signature, two little-endian 32-bit dimensions, and bottom-up RGBA bytes. Dimensions are bounded to 4096 on each axis and the payload must match exactly. Runtime code performs no PNG decoding. The converter preserves alpha and reverses rows to match the former PNG loader's Unity pixel orientation. Original PNGs are never changed.
