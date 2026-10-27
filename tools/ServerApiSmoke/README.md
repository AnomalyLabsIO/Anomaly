# Server API live fixture

Test-only ordinary LabAPI plugin; never put it in runtime or SDK release packages.
It is inert unless the server process receives `ANOMALY_SERVER_SMOKE_DIR` pointing
to a private evidence directory. Use the isolated loopback server/client staging
documented in `../EarlyAccessUiSmoke/README.md`.

```powershell
dotnet build tools/ServerApiSmoke/ServerApiSmoke.csproj -c Release -p:DeployLocal=false
./tools/ServerApiSmoke/Prepare-Assets.ps1 -OutputDirectory ./artifacts/server-api/assets-01 -PackIndexPath ./artifacts/assetbundles/qualified-fixture/anomaly-qualified-fixture.index.json
```

Set the isolated Anomaly plugin's `ContentPackDirectory` to that absolute content
root before starting the server. Preparation wraps the exported index/bundle in
a descriptor pack with explicit `Assets.Index`; loose pairs are not discovered.
Stage `Anomaly.ServerApiSmoke.dll` only in the
isolated LabAPI `plugins/global` directory. Anomaly initializes at highest
priority; this fixture uses Low and waits for completed pack indexing.

Link `SmokeMessages.cs` into the test client and register both message names.
Advertise capability `anomaly-smoke:client` version 1.0. Requests are authenticated
client-to-server; responses are authenticated server-to-client, both ordered and
bounded to 4096 bytes. Match `Token` when handling `SmokeResponse`.

Commands:

- `status` / `report`: JSON readiness, capability, native item-use count, content
  and motion notification counts, elapsed connected seconds and room digest.
- `prepare`: set Class-D role and 50 health; allow its FPC module to become ready.
- `exercise`: grant the custom usable item, select it, apply an explicitly owned
  small motion discontinuity, and return item ID/serial, destination and typed asset address.
- `texture:<netId>:<base64 UTF8 renderer path>:<materialIndex>`: apply the approved
  test texture to a client-discovered renderer. Empty paths are accepted.
- `look:<degrees>`: apply a same-position owned yaw change for screenshots.
- `clear`: remove fixture textures and release its motion owner state.
- `facility:<pristine|noop|lcz|hcz|entrance|atlas|planned-noop|planned-move|planned-remove|planned-checkpoint>`: select the next round's facility
  scenario. Restart the round separately, then request `report` after generation.

`Details` is a JSON object. Test asset identifiers are `mods/fixture/textures/checker`, `mods/fixture/audio/tone`,
item `anomaly_smoke:medkit`, vocabulary `anomaly_smoke_tone`. Texture hashes are
base64 SHA-256. The client should exercise the actual consent callback and native
item-use request; the fixture does not synthesize item-use events.

Evidence is `events.jsonl`, sorted `rooms.txt`, and, when available, the internal
read-only generation diagnostic `facility.json`. The diagnostic is reached only
by test reflection; it is not part of the public server SDK. No player identity
or secret keys are recorded. A client deliberately left awaiting content approval
must remain connected for at least 35 seconds; then verify decline denies the
grant, a subsequent approval activates the item/assets, and cached approval on
the third connection remains bound to the same manifest/catalog.

For a clean-install BouncyCastle check, stage a new private server and leave
`BouncyCastle.Cryptography.dll` only in its stock `SCPSL_Data/Managed` directory.
The fixture writes `bouncycastle-enabled.json` and
`bouncycastle-connected.json` with the loaded assembly identity, path, and
SHA-256. Require both captures to point to that stock copy, then complete an
authenticated approval cycle that exchanges fixture requests and replies.

The general room digest is SHA-256/base64 of newline-joined ordinal-sorted rows
`zone|name|shape|x|y|z|yaw`, with each position/yaw value multiplied by 100 and
rounded away from zero. Prefer `facility.json` for exact authoritative generation
comparison because it also records candidate and network identity selection.

Facility qualification uses fixed seed `12345` by default. Override it with
`ANOMALY_SERVER_SMOKE_SEED` and select a startup scenario using
`ANOMALY_SERVER_SMOKE_SCENARIO`. First run `pristine` and request `report` to save
`facility-baseline.json`. Legacy recipes and `planned-remove` read this file; use
`ANOMALY_SERVER_SMOKE_BASELINE` to reuse it from a different evidence directory.
The seed must match. Keep the baseline from the native, empty recipe path.

`noop` adds zero displacement to force the managed generator without changing
geometry; all room/candidate/transform/identity descriptors must match pristine.
`lcz` and `hcz` move, absolutely rotate, and skip three generic tiles in the chosen
zone. `entrance` moves both HCZ checkpoints and both Entrance checkpoints, moves
one Entrance tile and absolutely rotates another; compare checkpoint centroids
to ensure their original relative alignment remains intact. `atlas` erases one
generic LCZ tile in every atlas. Scenarios are separate because room skipping
changes the random sequence consumed by later zones. `recipe-<scenario>.json`
records exact requested operations and `facility-<scenario>.json` records results.
For each mutated run compare client/server results, repeat on late join, then
restart with the same seed and require a new generation ID with the same room
descriptors. Fixture setup failure writes `failure.txt` and cancels generation.

The `planned-*` scenarios subscribe to `MapEvents.LayoutFinalizing` from the
current `MapGenerating` callback. Other scenarios unsubscribe so `pristine`
continues to exercise the native empty-recipe path. `planned-noop` observes all
selected rooms without editing them and must exactly match pristine.
`planned-move` moves one room and rotates another in each atlas zone directly
from the current event, without reading an earlier baseline.
`planned-remove` removes the first occurrence of a repeated networked prefab.
Its diagnostic baseline groups rooms by their original child-identity ranges,
name and shape, because several different prefabs share `RoomName.Unnamed`.
Every surviving room must preserve its candidate, duplicate ordinal and identity
asset IDs. `planned-checkpoint` removes one HCZ-to-Entrance checkpoint after
alignment has been resolved; every other room, including Entrance, must retain
its original placement. These are placement tests, not playable replacement maps.
Each event writes `recipe-generation-<id>.json` as well as its scenario summary.
The additional standalone `planned-recipe-noop` scenario stages one zero-offset
legacy recipe before the whole-layout event and verifies its final descriptor
digest against the fixed-seed native baseline when `report` captures it. It
writes `recipe-noop-result.txt`; provide `ANOMALY_SERVER_SMOKE_BASELINE` when
starting this scenario in a new evidence directory. It does not add a cycle to
the normal matrix.

Run the automated map matrix with `EarlyAccessUiSmoke/Run-Smoke.ps1 -ServerApi
-ServerApiMode facility -LoopbackServer 127.0.0.1:27989` against its own private
server. Its 14 captures record pristine generation, a late reconnect, the no-op
adapter, LCZ, HCZ, Entrance, atlas edits and a repeated atlas round, all four
whole-layout scenarios, repeated planned removal and a late join to that edited
layout using native fast round transitions. After completion run
`Compare-Facility.ps1 -ServerEvidence <server-evidence> -ClientEvidence
<client-evidence> -Seed 12345` to verify snapshot binding,
exact binary room/candidate/transform/identity digests, requested mutation
effects, Entrance centroid alignment, and generation freshness. Descriptor
digests use fixed little-endian binary float32 values so differing JSON numeric
formatting between Mono and .NET cannot produce a false mismatch.

Each ready-state report also saves `facility-contents-generation-<id>.json`
on the server and `facility-contents-<cycle>.json` on the client. Schema 2
captures room-owned original network identities by prefab path, original and
assigned asset IDs, live net IDs and world poses; spawned room connectors by
net ID, type, asset ID, world pose and native door room associations; LCZ label
material names; and the separate generation-time `ConnectedRooms` cache.
Run `Compare-FacilityContents.ps1 -ServerCapture <server-json> -ClientCapture
<client-json> -NativeServerCapture <pristine-server-json> -NativeClientCapture
<pristine-client-json> -OutputPath <report-json>` for each cycle. Use
`-CompareNativeLabels` for the planned no-op cycle to verify both peers retain
their own pristine LCZ labels. For removal cases also supply `-BaselineCapture
<pristine-server-json> -RemovedRoomKeys <zone:x:y>`; the removed room's original
asset IDs must be absent from both live network registries, and neither cache
nor spawned connector may retain a room association.
Label correspondence uses the full ancestor names with generated sibling-index
suffixes removed and requires unique resulting paths on each peer. Native
connector cleanup destroys its spawnpoint objects only on the client, shifting
later sibling indices (`LCZ_TCross` has three such children before `Labels`).
Renderer presence and both actual material names remain strict comparisons.

Pose checks use 0.001 metres per axis and a normalized quaternion angular
distance of 0.1 degrees, treating `q` and `-q` as the same rotation. The native
seed-67890 baseline contained a 0.01344-degree locker difference:
`StructurePositionSync.Start` quantizes yaw in 5.625-degree steps, replacing
the initial Mirror spawn quaternion on the client. Float32 quaternion
normalization alone does not remove that real difference. An arbitrary yaw
produced a 2.01343-degree difference before the planned-generation preservation
patch; the 0.1-degree check deliberately rejects that case. The component's
presence and synchronized yaw code are retained for diagnosis.

The native seed-67890 baseline also had 234 server and 232 client directed
cache edges: the client omitted the reciprocal edge between LCZ rooms `1:3:2`
and `1:3:3`. The `LCZ_330` prefab's connector spawnpoint is under its `All`
network child. `SpawnableRoom.SetupNetIdHandlers` disables original network
children on clients before initial delivery, while
`RoomConnectorSpawnpointBase.ResolveConnectorConflicts` skips inactive
spawnpoints and only populates `ConnectedRooms` during generation. The cache
therefore can differ even after the server-spawned connector reaches the
client. Native and planned no-op captures had exactly the same cache on each
peer. The comparator reports every cache discrepancy and accepts one only
when the same directed discrepancy exists in the provided pristine baseline
for that seed. Actual spawned connector identities, poses and door room
associations must still match. A new unexplained cache discrepancy fails.
These captures do not assert general renderer/collider parity, runtime parent
paths, active flags, animated nonidentity children or non-atlas cache edges.

For another seed, set `ANOMALY_SERVER_SMOKE_SEED` in the PowerShell environment
before `Run-Server.ps1`, use fresh evidence directories, and pass the same value
to `Compare-Facility.ps1 -Seed`. The first pristine capture supplies that seed's
baseline. Build the core and both fixtures before preparing the isolated copies;
the run scripts also rebuild and stage them with `DeployLocal=false`.

The separate startup failure probe is enabled only by setting
`ANOMALY_SERVER_SMOKE_FAILURE_PROBE=1` before launching a new disposable isolated
server. Do not connect the matrix client to this process. The fixture shortens
one in-memory room catalog from `LayoutFinalizing`, forcing validation to fail
inside the native zone loop. A main-thread update callback checks one second
later that generation failed, no atlas rooms were instantiated, no
`MapGenerated`/`RecipeApplied` event fired, and no snapshot is published or
available. Require all eight assertions in `failure-probe.json` and a PASS in
`failure-probe-result.txt`; reflection/probe errors produce a failed report.
The scenario cannot be selected through fixture network commands. Stop this
server after the probe and remove the flag before any normal run. No disk prefab
or existing installation is changed; the catalog edit is process-local.

For recipient qualification, prepare a separate loopback server on port `27990`
and two separate fresh-profile games with the normal preparation scripts. Start
the server with `Run-Server.ps1`, verify its actual UDP listeners are loopback,
then launch both clients using `EarlyAccessUiSmoke/Run-Smoke.ps1 -ServerApi`
and `-LoopbackServer 127.0.0.1:27990`. Give one client
`-ServerApiMode recipients-sender` and the other `recipients-observer`, with
distinct evidence directories. These modes decline optional content.

The fixture registers two distinct authenticated players and exercises public
`ClientMessaging.Send` to the sender, `SendToAll` to both, and
`SendToAllExcept(sender, ...)` to the observer. Clients acknowledge each actual
receipt. Both clients assert their own receipt counts and wait for all expected
acknowledgements plus at least three seconds to observe excluded deliveries.
Unexpected recipients or duplicate delivery fail the fixture. Retain server
`recipients.json`, both clients' `client-recipients.json`,
`server-recipients.json`, `recipient-deliveries.jsonl`, assertions and results.
Stop only the PIDs recorded for these isolated instances after qualification.

Use client `-ServerApiMode round-content` to verify optional content across a
native round restart on one authenticated connection. Both player sessions must
activate the catalog, receive and use a custom item, receive vocabulary, apply
and restore a texture, and complete owned motion. The second session must have
a new hub and facility generation while retaining its Mirror connection and
server-local enrollment. Matching saved consent must reactivate without a new
prompt, and a completed transfer must leave no loading overlay.

For visual room inspection, use client `-ServerApiMode facility-views` against a
server running the intended generation scenario. The client selects a generated
generic room addressed by a transform recipe (preferring Entrance when available)
and an Entrance checkpoint. It sends `prepare` to become Class D, waits for its
movement module and camera, and uses `Hud.PushHide()` for the capture interval.
The harness also invokes the native HUD visibility delegate and temporarily
disables the native `RoundStart` UI component/window/background, preserving and
restoring their prior states. These capture-only controls do not start the round
or change the public HUD API.
The server command `view-room:<zone>:<x>:<y>:<yaw>` resolves exact generated atlas
coordinates through the native LabAPI `Room` wrapper, checks a standing position
against server geometry, and uses owned `PlayerMotion` to move and orient the
player. The helper captures each room at world yaw 0, 90, 180 and 270 after checking
client arrival and camera angle, then restores prior HUD visibility.

Keep `facility-view-targets-*.json`, per-view server response JSON and all eight
`facility-view-*.png` files together. Inspect those images for actual visible
geometry; a successful callback or screenshot file alone is insufficient.
The reusable `AddFacilityViews(cycle, scenario)` helper can also be queued after a
matrix generation and before its next restart. Rebuild/restage the server fixture
before using the new room-view command.

The Entrance fixture moves HCZ checkpoints by `(15,0,0)` and Entrance checkpoints
by `(0,0,15)`, producing a whole-zone centroid adjustment of `(15,0,-15)`.
These offsets preserve the native 15-metre horizontal room lookup grid. Arbitrary
whole-zone offsets can leave outlying SCP-079 interactables without a room even
when room transforms match on both peers; layout authors must preserve valid
native topology. A native stage exception rejects that generation and disconnects
its clients. Exact descriptor parity is a placement check, not a playability test.

Fixtures require completed bundle/index pairs from the external Unity authoring project. Pass `Prepare-Assets.ps1 -PackIndexPath` with an index exposing `mods/fixture/textures/checker` and `mods/fixture/audio/tone`. Preparation copies the completed files into a descriptor pack; it does not generate raw PNG/OGG assets or invoke FFmpeg. Rebuild both smoke mods from the current SDK.
