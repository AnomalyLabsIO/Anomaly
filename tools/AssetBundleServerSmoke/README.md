# Isolated AssetBundle server acceptance fixture

This ordinary LabAPI plugin is opt-in through `ANOMALY_BUNDLE_SERVER_SMOKE_DIR`.
It is never included in runtime packages. It uses the public server APIs and
the bounded `SmokeRequest`/`SmokeResponse` contract shared with the client probe.

Build the canonical qualified fixture with the offline Unity authoring project,
then stage a private copy of the dedicated server. The scripts require all
destination state under this repository's `artifacts/assetbundles` tree. They
copy independent files, preserve source files, and require loopback bindings.

```powershell
tools/AssetBundleServerSmoke/Prepare-Server.ps1 -SourceServerRoot E:/Projects/AnomalyLabs/Server/server -OutputDirectory artifacts/assetbundles/network-server-01
tools/AssetBundleServerSmoke/Run-Server.ps1 -ServerDirectory artifacts/assetbundles/network-server-01 -EvidenceDirectory artifacts/assetbundles/network-server-run01 -ContentPackDirectory artifacts/assetbundles/network-packs-01
```

The content root must contain `<namespace>/<pack>/pack.json` matching the exported
index identity and declaring `Assets.Index`; unlisted bundle/index pairs are not
discovered. Use the fixture preparation tooling to stage the descriptor and
its referenced files together.

Default port: `27989`. Preserve the stage's `AppData` between client joins to
keep the same server identity. Use a new evidence directory for each server run.
The fixture registers `fixture:coin` with the Sprite icon and
`ANOMALY_BUNDLE_TONE` with the indexed AudioClip duration. It creates a visual
before activation so initial and late-join snapshots can be inspected.

Commands are space-delimited:

| Command | Action |
| --- | --- |
| `status` | Report readiness, activated-client count, visual ID, item ID, audio duration and requested-file count. |
| `prepare` | Set the requesting player to Class-D for inventory checks. |
| `spawn` / `respawn` | Create the qualified visual near the player; recreate a disposed scope if necessary. |
| `update` | Move, rotate and scale the current visual. |
| `despawn` | Remove the current visual. |
| `dispose` | Dispose the owning scope and remove its visuals. |
| `grant` | Grant and select the custom coin through the activation gate. |
| `playtone` | Schedule the registered word through native LabAPI Announcer playback. |
| `texture <netId> <base64-path-or-dash> <material-index>` | Apply the indexed checker Texture2D to a chosen existing renderer. |
| `clear <netId>` | Restore overrides for that object. |
| `round` | Request the native silent round restart after replying. |

All mutations require `ContentReadiness.Activated`. `events.jsonl` records
public callbacks and command results; `server-player.log` includes the actual
worker indexing/copy time and transferred-file count/bytes. `runtime-inputs.json`
records the deployed fixture/core hashes. Verification still needs a real
client to inspect native assets, rendering, cleanup, consent and cached reconnects.

Content decisions and successful activation acknowledgements are recorded from
the public message event. A failed acknowledgement revokes admission before
that event can run; its exact failed keys are recorded by the core in
`server-player.log`. For negative acceptance, pair that rejection with the
client's failed acknowledgement witness and the absence of `activated` events.
