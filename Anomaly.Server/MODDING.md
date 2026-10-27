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
and `IPlayerEvent` arguments. Protocol 12 acquires and activates the exact required
catalog before Mirror Ready/AddPlayer or initial Anomaly setup can complete.
ContentReady publishes the adopted player's activated grant and may precede Connected
during adoption. Notifications are not replayed to late subscribers.

Content activation belongs to the current player instance. A native round restart
can keep the authenticated connection while replacing that instance; the new
player receives a fresh offer and raises ContentReady after activation again.
Previously approved matching server/catalog/manifest consent is reused with
cached files. Declining closes the connection. Consent, transfer, activation and
facility readiness share a fixed 120-second admission deadline; duplicate traffic or
unfinished generation replacement cannot extend it. Both peers must support protocol 12.
Declared Lua scripts require separate execution approval and finish top-level
initialization before the client can acknowledge activation or publish ClientReady.

Use LabAPI's existing gameplay events for item use, death, round changes, and
other normal game behavior. Custom items retain the stock template's behavior;
identify them with `CustomItems.TryGetItemKey`. Frozen definitions live for the
server process, so restart after changing or removing registrations.

## Author surface

| API | Purpose |
| --- | --- |
| `Api.Content.CustomItems` | Register definitions, resolve stable keys, inspect readiness, and grant activated items through `TryGiveItem(Player, string, out Item)`. |
| `Api.Auth.ClientModRequirements` | Require a companion client mod ID within a version range before connection admission. |
| `Api.Content.ContentPacks` | Capture descriptor-declared embedded content with `RegisterEmbedded(Assembly, resourceRoot)`; dispose registration during plugin shutdown. |
| `Api.Networking.ClientMessaging` | Send bounded authenticated messages to native LabAPI players. Register identical shared C# contracts in both runtimes, or use built-in pack-bound Lua carriers. |
| `Api.Events.Handlers.ClientEvents` | Connected, MessageReceived, CapabilitiesChanged, ContentReady, and MotionApplied notifications. |
| `Api.Features.ClientCapabilities` | Inspect the current client capability snapshot. |
| `Api.Features.PlayerMotion` | Apply an owned position/look/momentum discontinuity with increasing sequence values; reset only your owner ID. |
| `Api.Features.PlayerPoseOverrides` / `PlayerPoseRigs` | Own and stream cosmetic bone poses to compatible base clients; see [PLAYER_POSES.md](PLAYER_POSES.md). |
| `Api.Features.PlayerCombatPoseOverrides` | Independently authorize scoped native client combat queries; see [COMBAT_POSES.md](COMBAT_POSES.md). |
| `Api.Assets.AssetRegistry` | Inspect indexed typed addresses with `IsReady`, `Contains`, `TryGetKind`, and `TryGetAudioDuration`. |
| `Api.Assets.AssetOverrides` | Apply indexed Texture2D addresses to network objects and native LabAPI wrappers. |
| `Api.Assets.DecorativeVisualScope` | Own bounded decorative client instances from an ordinary LabAPI plugin. |
| `Api.Features.Announcer` | Add new vocabulary from indexed AudioClip addresses and validated clip durations. |
| `Api.Map.FacilityLayout` | Configure facility mutations before generation freezes its authenticated snapshot. |
| `Api.Events.Handlers.MapEvents` | Edit the complete selected layout with `LayoutFinalizing`; observe application with `RecipeApplied`. |

Call game-facing APIs on the server game thread. Asset-dependent state requires
the client's matching catalog activation; possession in its cache grants no access.
Pending consent does not reserve transfer slots or transfer bytes. The fixed
connection admission deadline still applies. Decline, timeout, or
transfer/activation failure revokes the grant and closes the connection.

## Content packs

Owners install `ContentPacks/<namespace>/<pack>/pack.json` under
`Config.ContentPackDirectory` (default `ContentPacks`, relative to the server
working directory). The descriptor explicitly composes one Lua entrypoint, an
asset index and/or typed server item definitions. Unlisted files do not execute
or transfer. There is no recursive Lua/index discovery, runtime module loader,
dependency resolution or implicit override order.

For example, the [server starter](../samples/HelloAnomalyServer/README.md) contains
`ContentPacks/example/panel/pack.json` declaring wrapper `example:panel`, version
`1.0.0`, and script component `example:panel_scripts` with entrypoint
`scripts/main.lua` and message schemas `scripts/messages.json`. The script address
is `mods/example/panel/scripts/main.lua`; messages use the component ID.
Version is diagnostic metadata only, not compatibility or load-order policy.

Embed the descriptor and its declared files with exact slash-delimited CLR
`LogicalName` values, then register during the normal LabAPI lifecycle:

```csharp
using Anomaly.Server.Api.Content;

private IDisposable _pack;

public override void Enable()
{
    _pack = ContentPacks.RegisterEmbedded(typeof(Plugin).Assembly, "HelloAnomalyServer.Panel");
}

public override void Disable()
{
    _pack?.Dispose();
    _pack = null;
}
```

The exact descriptor resource is `HelloAnomalyServer.Panel/pack.json`; the
entrypoint and schema resources are `HelloAnomalyServer.Panel/scripts/main.lua`
and `HelloAnomalyServer.Panel/scripts/messages.json`. No resource scanning or
dotted-name conversion occurs. See the [content-pack guide](../Anomaly.Client.Scripting/SCRIPT-PACKS.md)
for the complete descriptor, project resource declarations, strict paths,
typed `Data.Items` schema and limits.

Owner edits and registration changes publish complete immutable snapshots for
future offers through the existing authenticated transfer, cache and separate
content/execution consent flow. Active clients retain their approved snapshot
until replacement or disconnect. Disposal is not immediate client revocation.
Invalid candidates retain the previous complete snapshot; registration changes
hold new offers until their exact revision is published. Initial indexing failure
holds admission closed. Frozen item definitions cannot be changed by a refresh:
restart the server after definition changes. C# `CustomItems.Register` remains
supported alongside descriptor items; collisions reject atomically.
Disposing an embedded registration is idempotent even after its `Data.Items`
definitions freeze. It removes the authoring inventory, not native item
registrations. Subsequent candidates missing/changing those definitions reject,
leaving future admissions unavailable until restart; approved snapshots and
leases survive.


## Pack-bound Lua messages

Remote script execution is currently disabled until client memory containment is
qualified. Clients decline offers requiring scripts before consent or download;
the authoring and messaging interfaces below do not bypass that gate.

Use the existing `ClientMessaging` and `ClientEvents.MessageReceived` APIs. The
built-in `ScriptToClientMessage` and `ScriptToServerMessage` carriers are already
registered by Anomaly; a plugin does not register another network contract or
ship a client C# companion for these messages. Declare their case-sensitive
names, directions and primitive fields in the source pack's `Messages`
list. See the [script authoring guide](../Anomaly.Client.Scripting/SCRIPT-PACKS.md)
and [message contract guide](../Anomaly.Client.Scripting/MESSAGING-DESIGN.md).

For script component `example:panel_scripts`, declare `GetState` as server-to-client with
`RequestId: UInt32`, and `State` as client-to-server with `RequestId: UInt32` and
`Enabled: Boolean`. Then subscribe and unsubscribe using normal LabAPI plugin
lifecycle methods:

```csharp
using Anomaly.Server.Api.Events.Arguments;
using Anomaly.Server.Api.Events.Handlers;
using Anomaly.Server.Api.Networking;
using Anomaly.Shared.Networking.Messages;

public override void Enable()
{
    ClientEvents.ContentReady += OnContentReady;
    ClientEvents.MessageReceived += OnMessageReceived;
}

public override void Disable()
{
    ClientEvents.ContentReady -= OnContentReady;
    ClientEvents.MessageReceived -= OnMessageReceived;
}

private static void OnContentReady(ClientEventArgs ev)
{
    ClientMessaging.TrySend(ev.Player,
        ScriptToClientMessage.Create("example:panel_scripts", "GetState",
            new StateQuery { RequestId = 1 }));
}

private static void OnMessageReceived(ClientMessageReceivedEventArgs ev)
{
    if (ev.Message is not ScriptToServerMessage message ||
        message.PackId != "example:panel_scripts" || message.Name != "State" ||
        !message.TryRead<StateReply>(out var reply))
        return;

    // ev.Player is the authenticated sender. Validate application meaning here.
    LabApi.Features.Console.Logger.Info($"{ev.Player.Nickname}: {reply.RequestId}, enabled={reply.Enabled}");
}

public sealed class StateQuery { public uint RequestId { get; set; } }
public sealed class StateReply
{
    public uint RequestId { get; set; }
    public bool Enabled { get; set; }
}
```

The receiving Lua registers before ClientReady:

```lua
Messages.On("GetState", function(Message)
    Messages.Send("State", {
        RequestId = Message.RequestId,
        Enabled = true
    })
end)
```

Each send is checked against that recipient's retained approved pack, schema
and activation, then bound to the exact index hash and transfer session. A
server rescan cannot silently change an existing client's message contract.
`TrySend` returns false for an unavailable pack, invalid payload, wrong direction
or exhausted budget. `SendToAll` skips recipients without the matching grant.
Replies reach the existing public event only after the same checks. Anomaly
also rechecks approval between its internal handlers before publishing through
LabAPI's ordinary event dispatcher.

Messages use the existing reliable ordered authenticated transport. Each
direction has a per-activation aggregate limit of 20 messages/second (burst 40),
32 KiB/second (burst 64 KiB), plus 5 messages/second (burst 10) per pack/name.
Sends also stop when the recipient's native reliable queues contain 128 packets;
backpressure returns false without disconnecting the recipient.
Payloads, schemas and queued Lua delivery are bounded. Activation and disconnect
discard queued work and handler registrations. Local-only scripts without an
adopted server pack grant cannot use this bridge.

`RequestId` is ordinary application data, so track pending requests in your
normal plugin state and clear them when the player leaves. A valid schema does
not make a client claim authoritative: plugins must still check ownership,
permissions, distance, cooldowns and other gameplay rules.

## Client mod admission

To require a companion client mod, retain the registration returned during your
LabAPI plugin's `Enable()` and dispose it during `Disable()`:

```csharp
private IDisposable _clientRequirement;

public override void Enable() =>
    _clientRequirement = ClientModRequirements.Register("example.hello", "0.1.0", "0.2.0");

public override void Disable()
{
    _clientRequirement?.Dispose();
    _clientRequirement = null;
}
```

Add `using Anomaly.Server.Api.Auth;`. The minimum is inclusive and the optional
maximum is exclusive. Versions use two, three, or four numeric components; a
prerelease does not meet a stable range. Each active registration is checked
before the connection is accepted. A missing or wrong version yields a detailed
client-facing rejection. Anomaly.Server implicitly requires its matching
Anomaly.Client core version and protocol.

Ordinary MelonMods report their `MelonID` when declared, otherwise their
`MelonInfo` display name. Managed modules report their manifest ID. ID matching
ignores case, while the displayed name retains the client's spelling. The client sends the full reported list encrypted
during login. Its inventory has strict size limits; an inventory that cannot
fit is rejected locally instead of silently truncated. The admission list retains
every observed MelonMod and loaded managed module until process restart, including
failed or disabled modules. Plugins and ordinary UserLibs are not admission entries.

Server owners can set `BlockAnyAdditionalClientMods` or list exact IDs in
`BlockedClientModIds` in Anomaly.Server config. Both default to off/empty.
Block-any allows Anomaly core and mods required by active server plugins; an
explicit block always wins and a conflict with a requirement is logged. Restart
Anomaly.Server after changing owner config. Companion registrations take effect
for future connections as plugins register or dispose them. Multiple admission
failures are summarized in the client's rejection notice, with the full list
in server logs.

`AllowRiskyClientModifications` defaults to false. It rejects authenticated clients
reporting external authentication-path changes, incomplete monitoring, or inventory
divergence. Set it to true only when accepting those client-reported risks; required,
blocked, and additional-mod rules still apply. A blocked modification attempt or a
client's opt-in alone does not trigger this rejection. The audit is signed with the
inventory, but remains a client claim rather than attestation. Only the in-process
host shortcut bypasses dedicated-server admission; loopback and LAN clients do not.
Changes to risk or admission inventory after login require a fresh connection.

Texture overrides replace the selected material's base-color texture. The client
uses the shader's declared main texture, then `_BaseColorMap`, `_BaseMap`, or
`_MainTex` when available. Clearing an override restores the original texture on
the same material property; shaders without a supported texture slot are skipped.

## Offline asset packs

Build packs on the owner/modder's device with the matching Unity 6000.0.43f1
exporter for the Windows x64 client. Place the completed bundle/index under
`Config.ContentPackDirectory/<namespace>/<pack>/assets/`, and explicitly declare
the index in `pack.json` as `Assets.Index` with its matching `Assets.PackId`.
The bundle resolves relative to that index. The headless server validates bounded
metadata and hashes/copies completed files; it does not import PNG/OGG files,
decompress bundles or run Unity Editor. Publish completed pack updates atomically.

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

The application protocol is v8. Update client and server together; old peers are
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
