# Offline asset authoring kit

This directory is shipped as `authoring/` in the developer SDK. It is self
contained: no Anomaly source checkout, game install, signing keys or network
service is required. Use the SDK version matching the receiving clients/server.

For new packs, install Unity **6000.0.43f1** with Windows64 build support and create
an authoring project in that version. Put source images/audio/models under its
`Assets/` directory and create a JSON configuration:

```json
{
  "PackId": "example:panel",
  "Namespace": "example",
  "BundleFile": "panel.bundle",
  "LocalOnly": false,
  "Assets": [
    { "Address": "mods/example/icon", "Kind": "Texture2D", "SourcePath": "Assets/icon.png" }
  ]
}
```

From PowerShell, run:

```powershell
./authoring/Build-Pack.ps1 -AuthoringProject E:/Packs/Authoring -PackConfigPath E:/Packs/panel.json -OutputDirectory E:/Packs/export
```

Override `-UnityEditor` if needed. The script installs the versioned exporter and
its three Editor-only dependencies into the selected project, then starts Unity
in hidden batch mode. No source/game repository is opened. A successful export
writes a schema-2 index last and records `ANOMALY_PACK_EXPORT PASS` in the log.
The exporter stores the full validator's measured `DecodedBytes`, including
dependency textures and measured mesh buffers. Copy the resulting `.bundle` and
`.index.json` into an explicit `pack.json` descriptor; see the SDK's ASSETBUNDLES.md.

To keep an already compatible bundle unchanged while rebuilding its old index,
install .NET **8** and PowerShell 7, then run:

```powershell
./authoring/Upgrade-Index.ps1 -InputIndex E:/Packs/old.index.json -OutputIndex E:/Packs/new.index.json
```

Add `-Local` for a local-only pack, and optionally `-BundlePath` for a bundle in a
different directory. The tool validates complete length/hash/native contents
before emitting the measured summary. It never overwrites a source, bundle or
existing destination. Inspect the new index before deploying it under the
descriptor-selected filename. Its changed hash requires new exact-content consent.

The server authenticates metadata without decompressing bundles. Clients check
combined local/server residency before acquisition and independently measure
the downloaded bundle before native loading. Declarations do not bypass hard
limits; both overdeclared and underdeclared resource totals are rejected.
