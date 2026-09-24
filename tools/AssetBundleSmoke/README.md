# Native AssetBundle smoke gate

This opt-in MelonMod tests the actual staged Anomaly.Client runtime. The default
mode exercises the internal native adapter with a pinned owner-built texture
fixture. Supplying `-PackIndexPath` instead tests ordinary startup validation,
immutable cache copies, typed local lookup, and the production preloader/menu
bindings without assigning those bindings from the harness.

The optional `-QualificationIndexPath`, `-QualificationBundlePath`, and
`-QualificationExpectedSha256` arguments add an owner-pinned integration probe
for texture, sprite, mesh, compressed native audio playback, and trusted
decorative objects. It injects a local catalog into the production manager and
therefore does not prove authenticated transfer. The mesh frame uses a temporary
isolated HDRP camera and requires colored pixels at the expected location.
Audio evidence checks `AudioSource.isPlaying` and advancing sample position;
waveform inspection may be unavailable in the stripped player, and muted
playback does not prove human audibility.

`-Network` uses a separate run against the opt-in dedicated server fixture on
`127.0.0.1:27989`. Supply the local `.g7` index using
`-AdditionalLocalIndexPaths`. This mode tests actual authentication and consent,
decline without transfer, fresh approval/download, a cached reconnect, custom
icons and vocabulary, texture replacement/restore, visual lifecycle, a native
round restart, local/server/stock CASSIE precedence, and unload. It cannot be
combined with the locally injected qualification catalog. Start the private
server smoke first, then run the client with `-TimeoutSeconds 600`.

`-Network -NetworkMode negative` uses the dedicated server's bounded invalid-pack
fixture. It interrupts the first multi-chunk transfer, reconnects, and requires
managed rejection of the hash-matched invalid bundle before any new native load
or remote activation. Lifetime and process-memory snapshots are diagnostic
observations, not a performance benchmark.

`-LocalFallback missing -RawSkinDirectory <owner-skin-directory>` stages no local
pack. `-LocalFallback invalid -PackIndexPath <valid-skin-index>` stages that valid
pair plus a generated bounded invalid pair. Both require the original PNGs in
the private legacy skin directory to remain ignored, stock preloader/menu assets
to remain usable, and zero native bundle load attempts. Read
`local-fallback.json` for these results; the positive native-binding flags in
`native-gate.json` are intentionally false because no bundle was loaded.

The probe checks live Unity objects at both binding and capture, including the
preloader-to-menu transition. Repeated loads exercise retention deduplication.
`Unload(false)` must preserve assets and restore their original retention flags;
a fresh load followed by `Unload(true)` must destroy only that load's assets.
Managed wrapper references and matching instance IDs alone are insufficient
evidence that an asset survived Unity's unused-asset collection.

Build the current Debug client, bootstrap and harness with deployment disabled.
Build the two-texture authoring fixture separately. Then use PowerShell 7:

```powershell
dotnet build tools/AssetBundleSmoke/AssetBundleSmoke.csproj -c Debug -p:DeployLocal=false
./tools/AssetBundleSmoke/Prepare-Game.ps1 -SourceGameRoot ../Client -BundlePath ./artifacts/assetbundles/authoring-proof/anomaly-skin-proof.bundle -ExpectedSha256 <verified-build-hash>
./tools/AssetBundleSmoke/Run-Smoke.ps1
```

Staging is limited to a new directory under this repository's
`artifacts/assetbundles`; the default is `native-game-01`. Static game files use
hard links and must never be edited or updated in the stage. Loader files are
physical copies; mods, plugins, user libraries, Anomaly UserData, stock appdata,
and config are fresh. Source mods, identities and raw skins are not copied.
The local trust policy approves only the staged runtime and harness hashes.

The runtime stage excludes the removed BigGustave and NVorbis dependencies, and
the harness asserts they are absent. The runner requires matching ownership metadata and unchanged input hashes,
opens the child process hidden, supplies private appdata/config paths, waits a
bounded interval, and only stops the original PID after checking its executable
and start time. The harness checks the effective stock FileManager paths.
`Evidence/native-gate.json`, optional `qualification.json` or `network.json`,
`assertions.log`, `witness.jsonl`, and PNG frames
record separate native-load, preloader/menu target, capture and unload outcomes.
Screenshots need visual inspection; successful assignments alone do not prove
appearance. A missed preloader scene is explicitly recorded rather than invented.
The probe submits a fixture nickname through Anomaly's prompt and acknowledges
the stock photosensitivity warning through `Skip` after its normal countdown.
It uses only in-process game APIs and engine rendering, without desktop input.

These private artifacts and the harness must never enter a release package.
