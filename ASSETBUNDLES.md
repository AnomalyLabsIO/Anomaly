# AssetBundle content and local customization

Anomaly transfers externally authored Unity AssetBundles and their JSON indexes as content packs. The Windows client profile is SCP:SL 14.2.7 / Unity 6000.0.43f1. Build packs with the matching `Anomaly.AssetAuthoring` Unity project. Install the completed files on the server or client; the dedicated server does not need the Unity Editor.

## Installing a pack

Export one self-contained LZ4 bundle and one `*.index.json` file. Install them under `<namespace>/<pack>/assets/` inside the resolved server `Config.ContentPackDirectory` or client `Paths.ContentPacksPath` (`UserData/Anomaly/ContentPacks`). The default server root is `<LabAPI>/plugins/global/Anomaly/ContentPacks` for the packaged global install, or `<LabAPI>/plugins/<port>/Anomaly/ContentPacks` for a port-scoped install. Relative server overrides anchor to that scope's `Anomaly` directory, never the working directory; absolute overrides retain their location. Every pack requires `pack.json` at its root with `SchemaVersion: 1`, its exact directory-derived `PackId`, numeric `Version` such as `1.0.0`, and an explicit `Assets` section containing `PackId` and `Index` (for example `assets/panel.index.json`). The index `PackId` must match `Assets.PackId` and its namespace must match the wrapper. The bundle resolves relative to the index's `BundleFile`. A sole asset component may reuse the wrapper ID; a pack with scripts must use separate component IDs. Version is diagnostic only. Unlisted files and unreferenced indexes are not discovered. See the [complete descriptor contract](Anomaly.Client.Scripting/SCRIPT-PACKS.md).

Local asset changes take effect after restarting the client. Server indexing publishes complete immutable snapshots in the background. Existing connections retain their exact consented snapshot until cleanup; future offers use the new snapshot. Pack updates are not hot reload.

Do not edit a bundle after exporting its index. The index includes exact bundle length and SHA-256, platform/engine/game profile, typed roots, measured dimensions/audio frames/mesh counts, and optional decorative descriptions. The client hashes the complete index too, and the authenticated catalog and consent digest bind both files. Changing either requires a new exact-content approval. No cross-bundle dependencies are supported. An invalid server refresh keeps the previous complete snapshot and logs its error.

Refreshing files does not rebind existing plugin state. Registered words, texture overrides and spawned visuals retain the index hash they were created against; they are omitted for clients approved for a different version. Reapply texture overrides and respawn visuals after indexing a replacement pack. Custom words are registered for the server process lifetime, so restart the server after changing their pack. Existing connections are not offered replacement packs mid-session.

Install and validate the complete pack set before starting companion plugins. If the initial item catalog cannot be materialized, correct the pack files and restart the server; its frozen registrations are not retried by the file watcher. Publishing content updates during a restart keeps all players and registrations on the same version.

| Catalog address | Purpose |
|---|---|
| `game/skin/logo` | Local preloader and main-menu logo (`Texture2D`) |
| `game/skin/background` | Local preloader background (`Texture2D`) |
| `game/cassie/<name>` | Supported stock CASSIE replacement (`AudioClip`) |
| `mods/<namespace>/<asset>` | New texture, sprite, audio clip, mesh, or decorative description |

Addresses are lowercase ASCII with forward slashes. They are assigned with Unity's `AssetBundleBuild.addressableNames`; filename loading fallbacks are disabled. Dots remain meaningful: `game/cassie/.g7` is valid, while a whole `.` or `..` segment is rejected. Unknown `game/` targets, aliases, duplicate addresses, wrong types and namespace mismatches are rejected. Skin targets are local-only and cannot appear in server packs.

Approved server content takes precedence over matching local addresses for that connection. Disconnect revokes remote lookup, restores effective local/stock bindings, then unloads remote native resources. Local packs and skins persist across connections. The local set is validated and published atomically: a missing set uses stock assets, and any invalid pack rejects the complete local set before loading it. There is no fallback to raw PNG, OGG or WAV files.

## Supported authoring subset

Lua and typed server items can share the same [content-pack descriptor](Anomaly.Client.Scripting/SCRIPT-PACKS.md), through optional `Scripts` and `Data` sections.
AssetBundles remain restricted to the data types below.

- Textures: RGBA32, one mip, at most 4096 on each axis and 16 million pixels. Keep alpha for skin images. The initial logo/background conversion preserves source dimensions and pixel appearance.
- Sprites: standalone full-rectangle sprites with one texture dependency. Atlases, secondary textures, physics shapes, skinning and scriptable-object references are unsupported.
- Audio: Vorbis compressed in memory, mono/stereo, 8–96 kHz, at most 30 seconds. The exporter and validator must agree on native sample frames, sample rate and channels. Supported stock CASSIE replacements match the stock clip length within one sample and retain the original CASSIE scheduling duration; those two durations are often different. New words use `mods/` addresses and separate compiled registration.
- Meshes: static triangle meshes, UInt16/UInt32 indices, float32 position/normal/tangent/color/UV channels, at most 100,000 vertices and 200,000 triangles. No bones, blend shapes, compressed geometry, collision data or external buffers.
- Decorative visuals: bounded JSON hierarchies referencing meshes and optional textures from the same pack. Trusted client code constructs Transform, MeshFilter and MeshRenderer components with `opaque-unlit` or `opaque-lit` presets. Source GameObjects, materials and shaders are never included. Each hierarchy has at most 64 nodes and depth 16, with bounded transforms. Remote scenes allow 128 instances and one million triangles total.

Custom item models, animation, particles, physics, cameras, lights and arbitrary component/property assignment are outside this feature. Item icons and the existing supported renderer texture overrides remain available.

## Source media and runtime assets

Keep original images and audio unchanged. Import copies into the external authoring project, assign catalog addresses, validate and export on your own device. A PNG's disk folder or filename does not imply a replacement; explicitly assign `game/skin/logo` or `game/skin/background`. Assign stock CASSIE addresses from the matching game profile, preserving names such as `.g7`. Use a personal namespace for additions.

Keep source media outside the declared runtime payload. `Paths.ContentPacksPath`
is the completed local pack root; the runtime reads only descriptor-selected
indexes and their bundles, never raw authoring PNG/OGG files.

## Companion-mod APIs

Build client, server, Shared and companion mods against the same release SDK.

Client mods retrieve borrowed native objects on the Unity thread:

```csharp
using Anomaly.Shared.Assets;
using Anomaly.Client.Api.Assets;

var address = AssetAddress.Parse("mods/example/icon");
if (AnomalyResources.TryGetTexture(address, out var texture))
{
    // Bind texture to a mod-owned view. Release the binding on disconnect.
}
```

`TryGetSprite`, `TryGetAudioClip` and `TryGetMesh` use the same typed-address pattern. Do not destroy borrowed objects or retain remote references after disconnect. `AssetOverrideAppliedEventArgs.TextureAddress` identifies the bound address. Client-side `Announcer.AddWord(name, AssetAddress, category)` also accepts a new `mods/` audio address; its clip and duration follow the effective address layer. Stock CASSIE names cannot be registered as new words.

Server mods use `Api.Assets.AssetRegistry.IsReady`, `TryGetKind`, `TryGetAudioDuration` and `All` to inspect indexed metadata. Register custom item definitions during plugin enable with `CustomItemDefinition.IconAddress`; the completed catalog verifies its type. Wait for indexing before `Announcer.AddWord(name, AssetAddress, category)` or spawning decorative content. Duration is derived from the indexed clip, not supplied by the plugin. Texture override methods now take `AssetAddress`.

Create a `DecorativeVisualScope` for your namespace on the game thread, call `Spawn`, `Update` and `Despawn`, and dispose it in the LabAPI plugin's `Disable`. The server stores descriptions and transforms only. Publication waits for each recipient's matching Activated acknowledgment. Generation/revision-qualified snapshots cover late joins; subsequent updates are recipient-specific. Scope disposal and round/disconnect cleanup remove owned visuals. See `Anomaly.Server/MODDING.md` for the LabAPI surface.

## Validation and limits

The client validates the complete bundle before loading it into Unity. Unknown native types, script-backed objects, scenes, bundled shaders, external paths, mismatched indexes and malformed data are rejected. Export only the supported data types; a general-purpose Unity bundle is not compatible.

Limits include 64 MiB per encoded transfer file, 256 MiB per offered session, 256 files, 128 MiB expanded per bundle, 4096 serialized objects, 64 MiB decoded per asset and 256 MiB live decoded assets across local and remote packs. Texture/audio encoded budgets remain 16/32 MiB. A cached file still requires approval for use by a server.

Validation restricts what content can be loaded; it does not make arbitrary binary content risk-free. Only accept content from servers you trust. See the [validator reference](Anomaly.AssetValidation/README.md) for the exact supported export profile.

