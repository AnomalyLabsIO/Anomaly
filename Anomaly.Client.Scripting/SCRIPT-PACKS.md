# Client script packs

Server owners place Lua files in a directory. LabAPI plugins embed Lua resources
in their assembly and register that resource prefix. Anomaly snapshots those exact
bytes, generates the index, and sends both through its existing authenticated
content transfer, cache, consent, and connection-admission flow. Authors do not
build a Unity project or hand-write source hashes.

Remote execution remains disabled until memory containment is qualified.
`RemoteExecutionEnabled` follows `MemoryHardeningQualified`, currently false.
Required remote script offers fail closed before consent or download. The execution
flow below is exercised through local owner and isolated development paths;
client approval cannot enable the remote gate. Filesystem, OS, network, module loading, reflection, and automatic CLR
interop remain unavailable to Lua. Source delivery does not qualify CLR heap
containment. See [host constraints](README.md) and the [allocation audit](ALLOCATION-AUDIT.md).

## Server owner directories

`Config.ScriptDirectory` defaults to `ClientScripts`, resolved relative to the
server process working directory like the existing asset directory. An absolute
path fixes that location independently of the working directory. Each
`<namespace>/<pack>` directory is one logical pack:

```text
ClientScripts/
  example/
    panel/
      start.lua
      display.lua
      messages.json
```

The pack ID is `example:panel`. Lua files can live in subdirectories; their canonical
addresses are `mods/example/panel/<relative-path>.lua`. `messages.json` is optional
and declares custom message contracts; it is not executable. Other filenames are
not executable sources. Keep names canonical and use the lowercase `.lua` extension.

The server watches owner files and publishes immutable snapshots. A completed edit
affects subsequent offers; existing connections retain their exact approved
snapshot until offer replacement or disconnect. Do not use a file edit as an
immediate hot-reload or revocation mechanism.
Invalid input cannot replace the current valid snapshot.

## Embedded plugin resources

Use ordinary embedded resources in the LabAPI plugin project:

```xml
<ItemGroup>
  <EmbeddedResource Include="ClientScripts\**\*.lua" />
  <EmbeddedResource Include="ClientScripts\messages.json" />
</ItemGroup>
```

Opt the exact resource prefix in during the existing plugin lifecycle:

```csharp
using System;
using Anomaly.Server.Api.Scripting;

private IDisposable _clientScripts;

public override void Enable()
{
    _clientScripts = ClientScripts.RegisterEmbedded(
        "example:panel",
        typeof(Plugin).Assembly,
        "MyPlugin.ClientScripts",
        "MyPlugin.ClientScripts.messages.json"); // Optional descriptor resource.
}

public override void Disable()
{
    _clientScripts?.Dispose();
    _clientScripts = null;
}
```

Resource names use the assembly's exact CLR manifest spelling. Anomaly selects
only `.lua` resources beneath that prefix, maps relative dotted names to canonical
lowercase paths, and copies bounded source bytes when registering. It does not
scan arbitrary loaded assemblies for executable content. Omitting the final
argument means the pack declares no custom messages.

A registration owns the pack until disposed. Pack IDs must be unique across owner
and plugin sources. Disposing changes subsequent snapshots; active connections
keep their approved snapshot until offer replacement or disconnect. Registration
or disposal blocks new offers until a snapshot reflecting that registry revision is published.
If invalid owner files prevent publication, new admissions wait and fail closed at
the normal deadline while existing leases continue. A plugin requiring immediate
gameplay revocation must enforce that policy in its authoritative server code.

## Sources and generated index

Save sources as strict UTF-8 without a BOM or binary control characters. Tabs,
carriage returns, line feeds, Unicode text, and empty files are supported. NUL,
bytecode, and MoonSharp dumped chunks are rejected before interpreter creation.
Source bytes are not normalized, so newline or Unicode changes change identity.

Anomaly generates a bounded schema-1 script-pack index with `PackId`, `ApiVersion`,
`Sources`, and optional `Messages`. Each source entry contains its canonical
`Address`, exact `SourceBytes`, and lowercase `SourceSha256`. Authors supply only
source files and meaningful message schemas. The authenticated catalog and
manifest bind the exact index and source hashes; the client recomputes identity
on downloads and cache hits before execution.

| Resource | Limit |
| --- | --- |
| Source files | 8 across the active connection source set |
| One source | 64 KiB UTF-8 |
| Total source | 256 KiB across the active connection source set |
| Generated index | 64 KiB per pack |
| Message schemas | 32 per pack; 256 across the catalog |

Shared source content can use one cached physical file while retaining separate
logical pack/address identities. Empty files have the standard SHA-256 of empty
bytes and still count as sources.

## Approval and initialization

The following approval sequence applies only after the execution gate permits
the source path; required remote script offers currently fail before this step.
The client first approves the exact authenticated content offer before requesting
missing files. After source/index validation it presents a separate execution
approval with the server identity, source count, and byte count. Remembered content
consent does not authorize script execution. Declining required content or scripts
closes the connection.

After approval, the client validates the complete inventory, installs its pack-bound
`Messages` capability, and executes top-level chunks in ordinal `PackId`, then
ordinal `Address` order. Each source has a separate VM; no imports or cross-file
globals are supplied. Top-level code should register event/message handlers and
return promptly. It cannot wait for application messaging before admission.

Initialization finishes before Activated and before public `ClientReady`. The
existing facility/content/server-confirmation barriers still hold native remote
Ready and player creation, with a fixed fail-closed admission timeout. Initial
execution currently has 200,000 instruction fuel and a 100 ms wall deadline per
source, including compilation and host callbacks. Callbacks use the host's existing
bounded invocation and queue rules.

A stale offer, syntax error, terminal CPU/allocation failure, or another required
initialization failure revokes the whole set and closes the connection. Disconnect,
rekey, generation replacement, and shutdown revoke sessions, subscriptions, queued
callbacks, and messaging authority. Untrusted Lua unload hooks are not called.

Local owner sources at `<UserData>/Anomaly/ClientScripts/<namespace>/<pack>` use the
same generated index and source validation. They execute only during a verified
current nondedicated in-process host connection, before public `ClientReady`.
Menus, offline play, dedicated mode, and remote loopback connections do not satisfy
that proof. Local sources do not execute while joining a remote server.

See [connection lifecycle](CONNECTION-LIFECYCLE.md) and
[message schemas and Lua examples](MESSAGING-DESIGN.md).

## Proposed unified content-pack authoring

**Design recommendation, not an implemented format.** There is currently no
`pack.json` loader or `ContentPacks` discovery path. Keep the supported directories
and registrations above until a coordinated cutover exists.

Use a directory and bounded descriptor to group scripts, indexed assets and typed
server definitions; retain the existing authenticated catalog, manifest, transfer
and SHA-256 cache. The client receives missing typed blobs, not an archive to extract:

```text
ContentPacks/example/panel/
  pack.json
  scripts/
    main.lua
    messages.json
  assets/
    panel.index.json
    panel.bundle
  data/
    items.json
  authoring/
    icon.png
```

- The proposed descriptor identifies format version, logical pack ID/version,
  explicit executable entrypoints, message schema and asset-index paths. Original
  media under `authoring` is never offered to clients. Logical identity/version
  does not replace exact content hashes or prove namespace ownership.
- Initially lower one logical pack to existing uniquely named typed components,
  for example asset pack `example:panel_assets` and script pack
  `example:panel_scripts`. The current catalog rejects an asset and script pack
  sharing one pack ID. The adapter must generate their existing canonical
  addresses rather than assuming authoring disk paths are wire addresses.
- Explicit entrypoints require changing discovery/index generation: today every
  discovered `.lua` executes in its own VM. Do not silently introduce shared
  globals or unrestricted `require`. Start with one declared entrypoint; reusable
  source can be bundled offline within existing source limits. Any later runtime
  modules need a sealed, bounded, hash-bound pack-local resolver and defined
  initialization/cycle semantics within one VM.
- Assets remain the existing restricted, self-contained LZ4 Unity bundle plus
  typed index. No raw image/audio fallback, arbitrary prefabs, shaders or native
  cross-bundle dependencies. Avoiding ZIP extraction does not remove bundle
  validation, native loading or decompression.
- Proposed `data/items.json` belongs to a bounded **server-side** parser feeding
  the existing custom-item/catalog pipeline before registration freeze. It is not
  an existing downloaded data-pack feature or generic CLR/Unity deserializer.
- Reject duplicate IDs/addresses by default; do not copy implicit last-pack-wins
  behavior. Preserve supported stock targets, remote-over-local lookup and the
  local-only skin exclusion. Defer dependencies initially; later resolution must
  be finite, deterministic and authenticated, without arbitrary URL fetching.
- Preserve separate content/execution approval, the remote qualification gate,
  immutable connection snapshots, transactional activation and disconnect cleanup.
  Cache hits never confer authority. Any grouping/version/dependency/order field
  that affects client behavior or approval must be bound into authenticated
  metadata, requiring coordinated shared/server/client schema changes.

The smallest implementation is a bounded server authoring adapter with canonical
relative-path/reparse-point checks and existing component validators, followed by
authenticated common metadata only when runtime semantics require it. Do not add
a custom archive or parallel downloader. Existing physical-file/aggregate limits
still apply; Lua hashes may be shared, but asset index/bundle hash reuse across
separate catalog entries is currently rejected and is not general deduplication.

Minecraft's useful analogy is the manifest, namespaces and typed content:
[Bedrock pack manifests](https://learn.microsoft.com/en-us/minecraft/creator/reference/content/addonsreference/packmanifest)
separate resources/data/scripts and dependencies. Java also supports
[multiple server packs and explicit un-application](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-20-3).
Borrow those lifecycle concepts, not ZIP delivery or implicit override rules.
