# Default client logo and loading background

`personal-skins.bundle` and `personal-skins.index.json` are the exported
`personal:skins` pack from the adjacent `Anomaly.AssetAuthoring` project's
`Packs/skins.json`. The pair contains only `game/skin/logo` (2048 x 2048) and
`game/skin/background` (3328 x 1872) Texture2D roots for SCP:SL 14.2.7 / Unity
6000.0.43f1 / Windows x64. Its bundle SHA-256 and size are recorded in the index.

`release/Build-RuntimeRelease.ps1` validates this pair using the production
index and bundle validators, then generates
`UserData/Anomaly/ContentPacks/personal/skins/pack.json` with wrapper and sole
asset component ID `personal:skins`, version `1.0.0` and explicit
`Assets.Index: assets/personal-skins.index.json`. The exported pair is installed
under that pack's `assets/` directory. Custom skin selection derives the
namespace/pack directory from the verified index ID. This README is not packaged.
To update the images, export a new pair using `Anomaly.AssetAuthoring` and
replace both files together, or supply `-SkinIndexPath` to the release script.
Unity manifests and authoring logs are unnecessary at runtime.
