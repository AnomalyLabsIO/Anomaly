# AssetBundle content and local customization

Anomaly protocol 7 transfers externally authored Unity AssetBundles and their JSON indexes. The Windows client profile is SCP:SL 14.2.7 / Unity 6000.0.43f1. Build packs on the owner or modder's device using the separate `Anomaly.AssetAuthoring` Unity project. The dedicated server needs only completed files: no Unity Editor, importers, texture decoder, audio decoder, or bundle decompression runs there.

## Installing a pack

Export one self-contained LZ4 bundle and one `*.index.json` file. Copy both beside each other into the server's configured `asset_directory`, or into the client's `UserData/Anomaly/AssetBundles`. Subdirectories organize files only; the index's catalog addresses select their meaning. Local changes take effect after restarting the client. Server indexing happens in the background and publishes a complete snapshot atomically. Existing connections retain their exact consented snapshot until cleanup; future offers use the new snapshot.

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

- Textures: RGBA32, one mip, at most 4096 on each axis and 16 million pixels. Keep alpha for skin images. The initial logo/background conversion preserves source dimensions and pixel appearance.
- Sprites: standalone full-rectangle sprites with one texture dependency. Atlases, secondary textures, physics shapes, skinning and scriptable-object references are unsupported.
- Audio: Vorbis compressed in memory, mono/stereo, 8–96 kHz, at most 30 seconds. The exporter and validator must agree on native sample frames, sample rate and channels. Supported stock CASSIE replacements match the stock clip length within one sample and retain the original CASSIE scheduling duration; those two durations are often different. New words use `mods/` addresses and separate compiled registration.
- Meshes: static triangle meshes, UInt16/UInt32 indices, float32 position/normal/tangent/color/UV channels, at most 100,000 vertices and 200,000 triangles. No bones, blend shapes, compressed geometry, collision data or external buffers.
- Decorative visuals: bounded JSON hierarchies referencing meshes and optional textures from the same pack. Trusted client code constructs Transform, MeshFilter and MeshRenderer components with `opaque-unlit` or `opaque-lit` presets. Source GameObjects, materials and shaders are never included. Each hierarchy has at most 64 nodes and depth 16, with bounded transforms. Remote scenes allow 128 instances and one million triangles total.

Custom item models, animation, particles, physics, cameras, lights and arbitrary component/property assignment are outside this feature. Item icons and the existing supported renderer texture overrides remain available.

## Migrating source media

Keep original images and audio unchanged. Import copies into the external authoring project, assign catalog addresses, validate and export on your own device. A PNG's disk folder or filename does not imply a replacement; explicitly assign `game/skin/logo` or `game/skin/background`. Assign stock CASSIE addresses from the matching game profile, preserving names such as `.g7`. Use a personal namespace for additions.

`Paths.SkinsPath`, `Paths.OverridesPath` and `Paths.AudioOverridesPath` remain inert source-location helpers for migration tools. Runtime code no longer creates or reads those legacy folders. Use `Paths.AssetBundlesPath` for completed local packs.

## Companion-mod migration

This is a coordinated breaking protocol update from 5 to 6. Client, server, Shared and companion mods must be rebuilt from the same development SDK. Managed assembly versions remain `1.0.0.0` under the existing pre-release policy. There is no compatibility path for the former raw-file protocol.

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

`TryGetSprite`, `TryGetAudioClip` and `TryGetMesh` use the same typed-address pattern. Hash-based overloads are removed. Do not destroy borrowed objects or retain remote references after disconnect. `AssetOverrideAppliedEventArgs.TextureAddress` replaces the former byte hash. Client-side `Announcer.AddWord(name, AssetAddress, category)` also accepts a new `mods/` audio address; its clip and duration follow the effective address layer. Stock CASSIE names cannot be registered as new words.

Server mods use `Api.Assets.AssetRegistry.IsReady`, `TryGetKind`, `TryGetAudioDuration` and `All` to inspect indexed metadata. Register custom item definitions during plugin enable with `CustomItemDefinition.IconAddress`; the completed catalog verifies its type. Wait for indexing before `Announcer.AddWord(name, AssetAddress, category)` or spawning decorative content. Duration is derived from the indexed clip, not supplied by the plugin. Texture override methods now take `AssetAddress`.

Create a `DecorativeVisualScope` for your namespace on the game thread, call `Spawn`, `Update` and `Despawn`, and dispose it in the LabAPI plugin's `Disable`. The server stores descriptions and transforms only. Publication waits for each recipient's matching Activated acknowledgment. Generation/revision-qualified snapshots cover late joins; subsequent updates are recipient-specific. Scope disposal and round/disconnect cleanup remove owned visuals. See `Anomaly.Server/MODDING.md` for the LabAPI surface.

## Validation and limits

The client inspects the entire bundle before invoking Unity, including unexposed objects, preload closures and resource ranges. A single bounded background worker uses the pinned AssetsTools.NET core metadata parser only after checked framing/schema preflight. No texture extensions, Mono.Cecil or Cpp2IL are loaded for this feature. Unknown native types, script-backed objects, managed references, scenes, bundled shaders, external paths, mismatched indexes and malformed offsets/counts are rejected. The immutable bytes inspected are the bytes given to Unity; a held read lock prevents replacement during native use.

Limits include 64 MiB per encoded transfer file, 256 MiB per offered session, 256 files, 16 KiB chunks, 128 MiB expanded per bundle, 4096 serialized objects, 64 MiB decoded per asset and 256 MiB live decoded assets across local and remote packs. Texture/audio encoded budgets remain 16/32 MiB, charged conservatively by complete compressed UnityFS blocks intersecting each asset. Validation success is cached by bundle hash, index hash, validator build/profile and game profile; cache possession alone grants no remote authorization.

This is in-process validation, not process isolation. Unity, FMOD and the graphics driver still parse/use data inside the game process. The restricted data-only profile enforces the no-script policy; it does not claim arbitrary binary content is risk-free. See `Anomaly.AssetValidation/README.md` for the exact subset, parser boundary, tests and provenance.

## Qualification

Use `tools/AssetBundleSmoke` with a fresh isolated loadout for native and production skin loading, followed by separate audio/mesh fixtures and authenticated client/server acceptance. The two-texture skin pack alone is insufficient evidence for decoder removal. Runtime evidence belongs under `artifacts/assetbundles`; production packages exclude all smoke plugins and fixture bundles. Normal builds use `-p:DeployLocal=false` and never copy into an installed game.
