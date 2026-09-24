# Anomaly asset validation profile

`BundleValidator.Validate` accepts only `unity6000.0.43f1-win64-v1`. This is a deliberately narrow format shared with the Anomaly offline exporter, not a general Unity bundle importer. The server does not reference this assembly.

The validator copies a bounded seekable input into private memory, checks its expected SHA-256, strictly expands UnityFS raw/LZ4HC blocks, checks the complete serialized file, validates asset semantics and references, and returns a disposable immutable lease. Only `WriteVerifiedBytes` exposes the original validated content. The client writes that lease to a fresh protected file and retains a handle denying replacement/writes for the entire native bundle lifetime. Successful caching must bind the bundle hash, complete index hash, and validator profile ID. Hash agreement establishes identity with the consented bytes, not author trust.

The client serializes validation jobs and reserves validation memory separately from loaded assets: at most 64 MiB encoded input plus 128 MiB expanded data, bounded metadata/object scratch, and the existing live asset budget. There are at most 4,096 serialized objects (including the structural AssetBundle record), 4,095 public roots, 1,024 blocks, five canonical native classes, 65,536 parsed fields per object, and 4,096 elements per non-byte array. Raw byte arrays remain slices of the single expanded buffer. Expanded bytes and parsed object values do not escape validation.

Allowed asset classes are Texture2D (28), Mesh (43), AudioClip (83), and Sprite (213), plus the single structural AssetBundle (142). Every type record must match the embedded trusted record byte-for-byte before AssetsTools reads metadata. Missing type trees, managed/script/reference-type tables, external files, alternate schemas, unknown classes, hidden objects, extra roots, and unreferenced resource data are rejected. Every nonnull PPtr must resolve to the expected permitted native class inside the same serialized file. Public root preload lists must exactly match each root's reachable dependency set. Scripts, MonoBehaviours, GameObjects, materials, shaders, prefabs and sprite atlases are not accepted.

The initial content subset is:

- RGBA32 2D textures with one mip, dimensions at most 4,096 and at most 16 million pixels, with exact inline or `.resS` payload sizing. No texture platform blobs or streaming mipmaps.
- Standalone full-rectangle sprites with one texture dependency, bounded validated geometry, and no atlases, secondary textures, skinning, physics shapes or scriptable-object references.
- Triangle meshes with UInt16/UInt32 indices, bounded float32 position/normal/tangent/color/UV channels, at most 100,000 vertices and 200,000 triangles, and no compressed mesh data, bones, blend shapes, streamed geometry or collision buffers. Every index, stream range, channel and finite component is checked.
- Vorbis compressed-in-memory audio, one FSB5 sample, mono/stereo, 8–96 kHz, and at most 30 seconds. FSB framing, frame count, sample rate, channels, codec, supported setup chunk and packet lengths must agree with AudioClip and index metadata. Vorbis seek tables have at most 1,024 monotonically bounded entries; every referenced byte offset must identify an actual packet boundary. Other codecs require a separately reviewed profile extension.

The 16 MiB texture and 32 MiB audio compressed limits are charged by the **full compressed size of each UnityFS block intersecting that asset's serialized or resource ranges**, counted once per asset. Sprites include their texture dependency. Shared blocks can consequently be charged to multiple assets. This is intentionally conservative; expanded RGBA bytes are not mistaken for compressed PNG bytes. Decoded memory is charged once per serialized object, with textures at RGBA size and audio at float32 sample size, at most 64 MiB per asset and 256 MiB per bundle. The client separately applies its aggregate live-memory limits across packs and ownership layers.

No untrusted bytes reach `AssetsTools.NET.AssetBundleFile.Read`, `AssetsManager.LoadBundleFile`, `GetBaseField`, or a template built from an untrusted type tree. The core `AssetsFile.Read` runs only after all allocation-driving metadata and exact schemas are checked. Templates are created from embedded trusted schemas. The strict LZ4 decoder is independently implemented from the [official block format](https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md).

This enforces the supported data-only subset. It is **not a native-code sandbox**: Unity's asset parser, GPU driver and FMOD/Vorbis decoder remain in process. In particular, FSB/packet framing validation is not a complete Vorbis semantic decoder or a proof against an engine vulnerability. Negative tests never invoke Unity; runtime qualification must use trusted authored positive fixtures and a patched matching engine.

## Dependency provenance

The only additional runtime package is [AssetsTools.NET 3.0.4](https://www.nuget.org/packages/AssetsTools.NET/3.0.4), pinned exactly in the project and lock file. Its core package has no runtime NuGet dependencies. It is MIT licensed; upstream is [nesrak1/AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET).

Exact package SHA-256: `8c3e3844ec0dacd192cb158993deeb8f4e5ae08f31f994c7db8a55580a4548db`.
NuGet content hash is recorded in `packages.lock.json`. No upstream parser source was copied or modified. The released package was inspected directly; upstream development commits are not claimed as the source revision for 3.0.4. Packaging must retain the AssetsTools MIT notice alongside its distributed DLL.

## Canonical schema and test fixture provenance

The five `Profiles/*.bin` files are exact serialized type records from `Anomaly.AssetValidation.Tests/Fixtures/unity6000.bundle`, a locally authored Unity 6000.0.43f1 StandaloneWindows64 bundle with type trees retained. Source fixture SHA-256: `bb139f22934c6991c0c654db10bf90ea8435954ccb1593cb8d2298a67372a29e`.

The fixture consists of an authored 256×256 RGBA32 checker texture, a standalone `Sprite.Create` full-rect sprite (pivot 0.5, pixels-per-unit 64), a four-vertex/six-index quad mesh, and an authored 0.25-second 48 kHz stereo tone imported as Vorbis compressed in memory. It contains no third-party game media or executable assets. Its original export evidence is in the ignored `artifacts/assetbundles/schema-fixtures` directory. The type records were serialized from that trusted fixture with the pinned core library; their order is immaterial and their bytes are compared exactly. Updating the released Unity version, exporter subset, schemas or accepted codecs requires a new profile ID and positive/negative qualification.

`Fixtures/unity6000-g7.bundle` is a second authored tone: 72,000 frames at 44,100 Hz mono, 440 Hz tone frequency, matching the physical duration of the stock `.G7` clip without containing its audio. SHA-256: `650c15a42b6c06837578ffe9d06ac76199c892d2adbebae7a055040d049ccc70`. This longer clip qualifies Unity's bounded Vorbis seek table and 32-byte FSB alignment. Seek layout was cross-checked against the original [vgmstream FSB implementation](https://github.com/vgmstream/vgmstream/blob/master/src/meta/fsb5.c); no implementation source was copied.

Run isolated tests with `dotnet test Anomaly.AssetValidation.Tests/Anomaly.AssetValidation.Tests.csproj -c Debug -p:DeployLocal=false`. Tests include a committed small real Unity fixture and deterministic hostile mutations repacked with valid framing/hashes, so malformed objects reach the intended validation boundary. The larger local skin proof is an optional integration artifact and is not required on a fresh checkout.
