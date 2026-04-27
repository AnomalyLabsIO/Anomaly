# Player Customization Resources

Drop neutralized HDRP base-map PNGs in this folder. They are embedded into
`Anomaly.Client.dll` at build time (via the `<EmbeddedResource Include="..." />`
glob in `Anomaly.Client.csproj`) and loaded by `PlayerColorTracker` and other
customization features as `_BaseColorMap` overrides paired with a
`MaterialPropertyBlock` `_BaseColor` tint.

## Currently expected files

### `ClassD_jumpsuit_BaseMap_neutral.png` (Class D jumpsuit color customization)

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
