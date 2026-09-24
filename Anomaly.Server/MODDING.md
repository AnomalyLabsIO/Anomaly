# Modding against Anomaly.Server

Build an ordinary LabAPI `Plugin<Config>` against `Anomaly.Server.dll` and
`Anomaly.Shared.dll` from the matching developer SDK. The supported author API
is `Anomaly.Server.Api.*`. LabAPI owns your plugin's enable/disable lifecycle,
configuration, commands, permissions, gameplay events, and player/item wrappers.
Use the [HelloAnomalyServer starter](../samples/HelloAnomalyServer/README.md).

## Lifecycle and events

Load Anomaly at server startup, before the first facility generation. Loading or
replacing the core plugin after a map already exists is refused; restart the
server so both runtimes can receive a valid generation snapshot.

Register custom item definitions during `Enable`, before the catalog freezes.
Registration copies the definition and rejects invalid keys, vanilla base item
IDs, missing display names, invalid Unicode and oversized text before changing
the catalog. Keys, display names and descriptions are limited to 128, 512 and
4096 UTF-8 bytes respectively. A rejected registration leaves earlier valid
registrations intact; asset/template materialization is validated transactionally
when the catalog freezes.
Subscribe to `Api.Events.Handlers.ClientEvents` during `Enable` and remove the
same handlers during `Disable`. These use native LabAPI event-handler conventions
and `IPlayerEvent` arguments. Connected means the authenticated client completed
initial Anomaly setup; ContentReady separately means its exact optional catalog
is activated. Notifications are not replayed to late subscribers.

Content activation belongs to the current player instance. A native round restart
can keep the authenticated connection while replacing that instance; the new
player receives a fresh offer and raises ContentReady after activation again.
Previously approved matching server/catalog/manifest consent is reused with
cached files. An undecided or declined offer may prompt again for the new player.

Use LabAPI's existing gameplay events for item use, death, round changes, and
other normal game behavior. Custom items retain the stock template's behavior;
identify them with `CustomItems.TryGetItemKey`. Frozen definitions live for the
server process, so restart after changing or removing registrations.

## Author surface

| API | Purpose |
| --- | --- |
| `Api.Content.CustomItems` | Register definitions, resolve stable keys, inspect readiness, and grant activated items through `TryGiveItem(Player, string, out Item)`. |
| `Api.Networking.ClientMessaging` | Send bounded authenticated messages to native LabAPI players. Register identical shared message contracts in both runtimes. |
| `Api.Events.Handlers.ClientEvents` | Connected, MessageReceived, CapabilitiesChanged, ContentReady, and MotionApplied notifications. |
| `Api.Features.ClientCapabilities` | Inspect the current client capability snapshot. |
| `Api.Features.PlayerMotion` | Apply an owned position/look/momentum discontinuity with increasing sequence values; reset only your owner ID. |
| `Api.Assets.AssetRegistry` | Inspect indexed typed addresses with `IsReady`, `Contains`, `TryGetKind`, and `TryGetAudioDuration`. |
| `Api.Assets.AssetOverrides` | Apply indexed Texture2D addresses to network objects and native LabAPI wrappers. |
| `Api.Assets.DecorativeVisualScope` | Own bounded decorative client instances from an ordinary LabAPI plugin. |
| `Api.Features.Announcer` | Add new vocabulary from indexed AudioClip addresses and validated clip durations. |
| `Api.Map.FacilityLayout` | Configure facility mutations before generation freezes its authenticated snapshot. |
| `Api.Events.Handlers.MapEvents` | Edit the complete selected layout with `LayoutFinalizing`; observe application with `RecipeApplied`. |

Call game-facing APIs on the server game thread. Asset-dependent state requires
the client's matching catalog activation; possession in its cache grants no access.
Players may leave consent pending indefinitely without occupying transfer slots
or bytes. Approval begins the transfer inactivity deadline even before a file
request arrives. Decline keeps the connection; transfer/activation failure
revokes the grant and disconnects.

Texture overrides replace the selected material's base-color texture. The client
uses the shader's declared main texture, then `_BaseColorMap`, `_BaseMap`, or
`_MainTex` when available. Clearing an override restores the original texture on
the same material property; shaders without a supported texture slot are skipped.

## Offline asset packs

Build packs on the owner/modder's device with the matching Unity 6000.0.43f1
exporter for the Windows x64 client, then copy completed `.bundle` and
`.index.json` pairs into `Config.AssetDirectory`. The headless server only
validates bounded metadata and hashes/copies completed pairs. It does not import
PNG/OGG files, decompress bundles, or run Unity Editor. Publish the index last,
preferably using an atomic rename after both files have finished copying.

The index selects every asset by a canonical `Anomaly.Shared.Assets.AssetAddress`.
Use `mods/<namespace>/...` for new assets and supported `game/cassie/<name>`
addresses for stock CASSIE replacements. `game/skin/...` packs are local client
content and are rejected by the server index. Each index declares the exact
supported game profile and bundle hash. Duplicate pack IDs/addresses or invalid
pairs reject the candidate generation and retain the previous complete one.

Indexing and file reads happen on workers. Wait for `AssetRegistry.IsReady` on
the game thread before calling APIs that need indexed assets. Custom item
definitions may register earlier: `IconAddress = AssetAddress.Parse("mods/example/icons/coin")`
is checked against the captured pack generation when the first offer freezes.
Icons accept Texture2D or Sprite. `AssetOverrides.SetTexture` accepts a Texture2D
address. `Announcer.AddWord("EXAMPLE", AssetAddress.Parse("mods/example/audio/word"))`
derives its duration from the index's sample count/rate. It rejects stock names;
stock replacements preserve stock scheduling durations and must match the
original clip duration within one sample. The standalone pause `.` is excluded.

Each offer pins an immutable private spool snapshot for that client's session.
Changing/removing the source pair affects later offers. Already active clients
keep their original bytes and metadata; state using a different index generation
is not sent to them. Transfer copies are bounded by the manifest limits and a
512 MiB live spool budget. Normal session/plugin cleanup releases snapshots;
after an abnormal server exit, remove obsolete `.anomaly-spool` files while the
server is stopped if disk cleanup is needed.

Create `new DecorativeVisualScope("example")` in your LabAPI plugin and keep the
scope until `Disable`, where you must dispose it. `Spawn(address, position,
rotation, scale)` returns a `uint` instance ID; `Update` changes its transform and
`Despawn` removes it. A scope may spawn only its own `mods/example/...` visual
definitions. The server holds lightweight state: no decorative Unity scene
objects. Clients create trusted renderer components from bounded descriptions,
with fixed `opaque-unlit`/`opaque-lit` material presets; no bundled scripts,
prefabs, shaders, colliders, physics, or gameplay behaviors are instantiated.

Limits are 128 instances and 1,000,000 referenced triangles across all scopes.
Activation sends a generation/revision snapshot, followed by ordered updates;
late clients receive current state. A scope's disposal removes its instances.
Round restart clears all instances, and disconnect revokes that client's state.
These visuals do not supply collision or gameplay logic; implement those through
normal server/game APIs where needed.

## Facility generation

Use LabAPI `ServerEvents.MapGenerating` for seed and atlas/recipe authoring through
`FacilityLayout`. Use Anomaly `MapEvents.LayoutFinalizing` when your plugin needs
to inspect the selected rooms across the complete facility before deciding which
ones to move or remove. The latter runs for unchanged and custom layouts, before
the final authenticated snapshot is published and before native `MapGenerated`.
Subscribe in `Enable` and unsubscribe in `Disable`; no room-change preregistration
is required.

`FacilityLayoutFinalizingEventArgs.Rooms` is a fixed collection of
`FacilityRoomPlacement` descriptors. `Zone`, `Coords`, `Name`, and `Shape` identify
the selected room. `OriginalPosition` and `OriginalRotationY` describe its pose
at event entry, including earlier recipe changes. Assign `Position` for an
absolute final world position, `RotationY` for absolute world yaw in degrees,
or `IsRemoved` to omit the room and its vanilla contents. Moving a room does not
change its atlas identity. Final edits preserve the selected identities of
unaffected rooms; they do not reroll later room choices.

The scope is LCZ, HCZ, and Entrance; Surface generation stays native. Removed
rooms never instantiate their geometry, lights, colliders, or owned network
objects. Surviving neighbours still run their own connector and fallback-door
logic. Arbitrarily rotated planned rooms retain the full server spawn rotation
for their structure objects instead of the stock quantized structure yaw.

Handlers run synchronously on the server game thread. Later handlers see earlier
edits, and the last assignment wins, including setting `IsRemoved` back to false.
LabAPI logs handler exceptions and continues; valid assignments made before an
exception remain. Invalid nonfinite poses are rejected before assignment.
Keep any related edits together in one handler and coordinate conflicting plugins.
After the event ends, retained descriptors remain readable but setters reject
changes, including calls from tasks, delayed callbacks, or `MapGenerated`.

Descriptors are not live LabAPI room wrappers or Unity objects. The server
validates and freezes the final layout for initial and late clients before they
generate their maps. A rejected or failed generation does not admit clients
into a different layout. Plugin authors own route connectivity and gameplay
consequences: final edits do not repair door connections or reposition other
rooms. Linking checkpoints may be removed after the initial zone alignment has
been resolved; the other planned placements retain that alignment. Removing a
room reserves its space, but does not provide a replacement model: custom mesh,
collider, and gameplay-object delivery remain the replacement plugin's concern.
`RecipeApplied` remains an observation raised during LabAPI `MapGenerated` and is
not an editing window.

The [server starter](../samples/HelloAnomalyServer/README.md) includes a disabled
by default example that finds a selected room, then moves or removes it. Try it
only on a test server after choosing the desired room and replacement behavior.

## Migrating development plugins

This is a deliberate pre-release compatibility reset while managed versions
remain `1.0.0` (`1.0.0.0` in assembly metadata). Rebuild every companion against
the new SDK. Existing releases and tags are unchanged. Future frozen public
contracts follow semantic versioning and intentional baseline review.

- Move author-facing imports from `Anomaly.Server.Content`, `.Features`,
  `.Assets`, `.Map`, and `.Networking` into `Anomaly.Server.Api.*`.
- Replace `Content` with `CustomItems` and `GiveItem` with `TryGiveItem`.
- Replace `ItemLogicBase` with native LabAPI item events.
- Replace raw `ReferenceHub`/connection public parameters with LabAPI `Player`
  and other native wrappers.
- Replace messaging callbacks with `ClientEvents.MessageReceived` and its
  player/message event arguments. Use `ClientMessaging.Send`/`TrySend` and
  `ClientEvents.Connected` instead of the runtime transport's readiness callback.
- Replace content, capability, and motion callbacks with their `ClientEvents`
  counterparts; remove global reset, initialization, and teardown calls.
- Replace raw-path/hash asset calls and `IconPath` with typed `AssetAddress`
  values and `IconAddress`; remove caller-supplied CASSIE clip durations.

### Anomaly.Portals migration example

The existing `Anomaly.Portals.Server/PortalServerController.cs` consumes the old
server surface. Rebuild that companion against the matching SDK and migrate its
calls as follows; its normal LabAPI lifecycle and gameplay subscriptions remain
the integration points.

| Existing Portals use | Updated consumer code |
| --- | --- |
| `Anomaly.Server.Features` | Import `Anomaly.Server.Api.Features`. |
| `Anomaly.Server.Networking.AnomalyMessaging` | Import `Anomaly.Server.Api.Networking.ClientMessaging` and `Api.Events.Handlers.ClientEvents`. |
| `AnomalyMessaging.ClientReady` / `OnClientReady(ReferenceHub)` | Subscribe to `ClientEvents.Connected` with `ClientEventArgs`; use `ev.Player`. |
| `AnomalyMessaging.MessageReceived` / `(ReferenceHub, IAnomalyMessage)` callback | Subscribe to `ClientEvents.MessageReceived` with `ClientMessageReceivedEventArgs`; use `ev.Player` and `ev.Message`. |
| `AnomalyMessaging.SendToClient(hub, message)` | Call `ClientMessaging.Send(Player.Get(hub), message)`, or handle a false return from `TrySend`. |
| `ClientCapabilities.Supports(hub, capability)` | Pass a LabAPI `Player`; `CurrentRoundEpoch` remains on the moved feature class. |
| `PlayerMotion.TryApply(owner, hub, motion, out failure)` / `Reset(hub, owner)` | Pass a LabAPI `Player` to the same feature methods. |

Use `LabApi.Features.Wrappers.Player.Get(hub)` at boundaries if Portals retains
its internal hub-keyed motion/collision state. Keep matching event removals in
`Disable`. `Plugin.Instance.IsOperational` remains available for its existing
startup dependency check. Register its shared message contracts in both runtimes
before sending and rebuild the client/shared companion assemblies with the new
protocol; a namespace edit alone does not make old binaries compatible. These
are migration instructions, not a claim that the separate Portals repository
has been migrated or qualified.

The application protocol is v6. Update client and server together; old peers are
rejected during the handshake. The authenticated facility snapshot is required
before client procedural generation; it is independent of content consent.

## Verification

Build the affected server and companion without deployment. The metadata-only
baseline check is:

```powershell
dotnet run --project tools/PublicApiBaseline/PublicApiBaseline.csproj -- Anomaly.Server/bin/Debug/Anomaly.Server.dll Anomaly.Server/PublicAPI.Shipped.txt
```

Only use `--update` after reviewing the intentional public change. The release
gate checks both client/server baselines and compiles all extracted SDK starters.
Automated compilation does not establish live LabAPI reload, client rendering,
map generation, or authenticated networking behavior.
