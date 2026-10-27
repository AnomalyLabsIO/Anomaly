# Content packs and client scripts

Content packs compose an explicit Lua entrypoint, an indexed Unity AssetBundle,
and typed server item definitions in one bounded authoring descriptor. Anomaly
captures declared content and uses its existing authenticated catalog, manifest,
transfer, SHA-256 cache and admission flow. There is no ZIP/custom archive or
parallel downloader. Unlisted files never execute or transfer.

**Remote script execution requires qualified Windows process isolation.**
`RemoteExecutionEnabled` follows `RemoteIsolationQualified`, established by actual
low-privilege AppContainer (LPAC)/job enforcement and probes, not `MemoryHardeningQualified` (still false).
Required offers fail closed before consent/download when isolation is unavailable;
content and separate execution approval are both required when it is available.
Unsupported platforms and Wine have no in-process fallback. Verified nondedicated
in-process hosting remains the separate local owner execution path.
Filesystem, OS, networking, reflection, automatic CLR interop and module loading
remain unavailable to Lua. Source delivery does not qualify CLR heap containment.
See [host constraints](README.md) and the [allocation audit](ALLOCATION-AUDIT.md).

## Descriptor and directories

Server `Config.ContentPackDirectory` defaults to `ContentPacks`, relative to the
server process working directory; an absolute path fixes the location. Local
client packs use `Paths.ContentPacksPath`, `<UserData>/Anomaly/ContentPacks`.
Discovery visits exactly `<namespace>/<pack>/pack.json`, not recursive Lua or
asset-index trees. Every pack directory must contain its descriptor. Root and
namespace files are ignored; only the root `.anomaly-spool` directory is skipped.

```text
ContentPacks/example/panel/
  pack.json
  scripts/main.lua
  scripts/messages.json
  assets/panel.index.json
  assets/panel.bundle
  data/items.json
  authoring/icon.png
```

A descriptor composing all three sections is:

```json
{
  "SchemaVersion": 1,
  "PackId": "example:panel",
  "Version": "1.0.0",
  "Scripts": {
    "PackId": "example:panel_scripts",
    "EntryPoint": "scripts/main.lua",
    "Messages": "scripts/messages.json"
  },
  "Assets": {
    "PackId": "example:panel_assets",
    "Index": "assets/panel.index.json"
  },
  "Data": { "Items": "data/items.json" }
}
```

`Scripts`, `Assets` and `Data` are optional, but at least one must exist.
`Scripts.Messages` is optional; other shown fields are required in their section.
Unknown, duplicate and null fields are rejected. `Version` must be numeric
`major.minor.patch`; it is authoring/diagnostic metadata, not a compatibility,
load-order or approval policy. Exact hashes still bind client content.
There are no dependencies, runtime modules, implicit overrides or hot reload.

The directory identity must exactly equal the wrapper `PackId` (`example/panel`
for `example:panel`), using lowercase ASCII without case normalization.
Script and asset component IDs are explicit, share the wrapper namespace and
must be globally unique across components and owner/embedded inventories.
Use separate IDs when both components exist. A sole component may reuse its
wrapper ID, including an existing exported skin index ID. The asset index's
`PackId` must equal `Assets.PackId`, and its `Namespace` must equal the wrapper
namespace. Message carriers use the **script component ID**, here
`example:panel_scripts`, not the wrapper ID.

Descriptor paths are canonical pack-relative lowercase ASCII (`a-z0-9._-/`), at
most 255 characters and eight segments. Empty, `.` and `..` segments, backslashes,
rooted paths, drives and traversal are rejected. Metadata paths must be distinct.
Entrypoints end in `.lua`, messages/items in `.json`, and asset indexes in
`.index.json`. The bundle path derives from the index-relative `BundleFile` and
must remain inside the pack. Filesystem roots and every path ancestor are checked
for links/reparse points. Original media under `authoring` is not traversed or
included merely because it exists.

## One entrypoint and explicit assets

Each script component declares exactly one Lua entrypoint and gets one VM; no
imports, `require`, runtime modules or shared globals are supplied. Bundle reusable source
offline within the existing source limits. The example's source address is
`mods/example/panel/scripts/main.lua`: it derives from the wrapper ID plus the
entrypoint, not from the script component ID. Only this source executes.
`Scripts.Messages` refers to the existing `{"Messages":[...]}` schema file.

`Assets.Index` explicitly selects the existing typed asset index. Keep the
exported self-contained LZ4 bundle beside that index when `BundleFile` names a
sibling. No raw-image/audio fallback, arbitrary prefabs, shaders or native
cross-bundle dependencies are added. Local-only skin exclusions, supported stock
targets and remote-over-local address lookup remain unchanged. See
[asset authoring and native limits](../ASSETBUNDLES.md).

## Typed server items

`Data.Items` selects a server-side JSON file, not downloadable arbitrary data or a
CLR/Unity object deserializer. The LabAPI server plugin materializes definitions;
copying a pack to the client's local directory validates its data but does not
register server items in a nondedicated stock host.

For example, `data/items.json` can contain:

```json
{
  "SchemaVersion": 1,
  "Items": [
    {
      "Key": "example:coin",
      "BaseTemplate": "Coin",
      "DisplayName": "Example coin",
      "Description": "An ordinary coin with custom presentation.",
      "IconAddress": "mods/example/coin"
    }
  ]
}
```

`Key`, `BaseTemplate` and `DisplayName` are required. `Description` is optional
and defaults to empty; `IconAddress` is optional. Unknown, duplicate and null
fields are rejected. `BaseTemplate` is the exact supported `ItemType` enum name,
not a numeric value. Keys and icons belong to the wrapper namespace. An icon must
be a Texture2D or Sprite declared in this same captured pack's `Assets` index;
omit the icon for a data-only pack. Limits are 127 definitions and 64 KiB per
items file, with 128/512/4096 UTF-8 bytes for key/name/description. The server also
checks the aggregate item count.

Definitions join the existing main-thread catalog transaction before native
registration freezes. C# `CustomItems.Register` remains supported alongside data
items; collisions reject atomically. Definitions live for the server process:
restart after changing/removing them. Changes to frozen definitions reject future
publication/offers rather than hot-registering or unregistering native items.

## Embedded plugin resources

The [server starter](../samples/HelloAnomalyServer/README.md) embeds a script-only
descriptor. Declare exact CLR logical names, including forward slashes:

```xml
<ItemGroup>
  <EmbeddedResource Include="ContentPacks\example\panel\pack.json" LogicalName="HelloAnomalyServer.Panel/pack.json" />
  <EmbeddedResource Include="ContentPacks\example\panel\scripts\main.lua" LogicalName="HelloAnomalyServer.Panel/scripts/main.lua" />
  <EmbeddedResource Include="ContentPacks\example\panel\scripts\messages.json" LogicalName="HelloAnomalyServer.Panel/scripts/messages.json" />
</ItemGroup>
```

Register during the ordinary LabAPI plugin lifecycle:

```csharp
using System;
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

Anomaly reads `resourceRoot.TrimEnd('/') + "/pack.json"`, then that exact root
plus each canonical declared path. It does not scan resources, transform dotted
names or discover other assemblies. Registration captures bounded immutable
metadata, source and any declared bundle bytes. Dispose the registration at
plugin shutdown. Registration/disposal blocks new offers until the matching
registry revision is published. Existing approved leases remain valid until
replacement or disconnect; disposal is not immediate gameplay revocation.

Disposal is idempotent even when an embedded pack supplied `Data.Items` that
already froze into native registrations. It removes the authoring registration
and invalidates the publication revision; it does not unregister frozen native
items. A subsequent candidate missing or changing those definitions is rejected,
so future admissions remain unavailable until server restart. Existing approved
snapshots and leases survive.

## Limits, snapshots and consent

Save sources as strict UTF-8 without a BOM, NUL, binary control characters,
bytecode or MoonSharp dumped chunks. Tabs, newlines, Unicode text and empty files
are supported. Exact source bytes are not normalized. Anomaly generates the
existing schema-1 script index with component `PackId`, `ApiVersion`, `Sources`
and optional `Messages`; each source has an address, byte count and SHA-256.
The client checks authenticated identity on downloads and cache hits.

| Resource | Limit |
| --- | --- |
| Descriptor | 16 KiB |
| Discovered packs / scanned entries | 128 / 4096 |
| Source files | 8 across the active connection source set |
| One source / total source | 64 KiB / 256 KiB UTF-8 |
| Generated script index | 64 KiB per component |
| Message schemas | 32 per component; 256 across the catalog |
| One encoded transfer file / offered session | 64 MiB / 256 MiB |

The server watches owner content and publishes complete immutable snapshots.
Invalid input cannot replace a valid snapshot. Active connections retain their
exact approved bytes; edits affect future offers, not immediate hot reload.
If a registration revision cannot be published, new admissions wait and fail
closed at the normal deadline while existing leases continue. Publish completed
pack updates atomically where possible. Enforce urgent gameplay revocation in
authoritative server code.

After the execution gate permits a path, content consent precedes missing-file
requests. Separate execution consent displays server identity and source/byte
counts. Remembered content consent or cached bytes never authorize execution.
Declining required content/scripts closes the connection. Approved sources
initialize in ordinal component `PackId`, then address order, before Activated
and public `ClientReady`; native facility/content/server-confirmation barriers
and the fixed admission timeout remain. Top-level code should register handlers
and return promptly, not wait for messages. Initial execution has 200,000
instruction fuel and a 100 ms wall deadline per source, including compilation and
host callbacks; callback invocation and queues retain their existing bounds.

A stale offer, syntax error, terminal CPU/allocation failure or required
initialization failure revokes the whole set and closes the connection.
Disconnect, rekey, generation replacement and shutdown revoke sessions,
subscriptions, queued callbacks and messaging authority, without Lua unload hooks.

Local client scripts use the same descriptors and source validation, but execute
only during a verified current nondedicated in-process host connection before
`ClientReady`. Menus, offline play, dedicated mode and remote loopback do not
satisfy host proof. Local scripts never execute while joining a remote server.
Local activation zero cannot exchange authenticated pack messages.

See [connection lifecycle](CONNECTION-LIFECYCLE.md) and
[message schemas and Lua examples](MESSAGING-DESIGN.md).

## Cutover acceptance (2026-10-26)

The real game/loader smoke passed through both console `host` and Create Server
with three explicit script descriptors. The sample rendered local player/round
text; independent owners retained literal, noninteractive output and separate
clear/expiry. Both hosted connections exercised queued native-query callbacks,
disconnect and fresh rehost sessions. The loader also reported the declared local
skin component loaded with both texture roots. Screenshots were inspected and
the reversible deployment/profile restoration verified protected hashes.

A fresh isolated dedicated server materialized owner and exact-name embedded
`Data.Items` definitions into distinct native Coin templates. Repeated disposal
of the embedded registration preserved frozen templates, invalidated new offers
and rejected the subsequent missing-definition candidate. No client connection
or remote Lua execution was involved in that server probe.

Contract/lifecycle suites, both public API baselines, release tooling and all
packaged SDK starters passed. A selected skin index with spaces/uppercase in its
source filename was accepted and staged under a canonical declared path. Local
evidence is retained in `artifacts/content-packs-cutover-20261026/`; test-only
probe sources and deployment DLLs were removed. This does not qualify hostile
remote Lua, native/CLR heap limits or multiplayer churn.
