# Anomaly release production

To build the client, server, and developer SDK release artifacts locally, run this from the repository
root in PowerShell 7:

```powershell
./release/Build-RuntimeRelease.ps1 -Version 1.0.0
```

The output directory, `artifacts/runtime-1.0.0/`, contains:

- `anomaly-client-1.0.0.zip`
- `anomaly-server-1.0.0.zip`
- `anomaly-sdk-1.0.0.zip`
- `SHA256SUMS`, covering all three ZIPs

The script builds
Bootstrap, Client, Api, Scripting, Shared, AssetValidation, and Server in Release with
`DeployLocal=false`. It works from the current checkout, including uncommitted
changes, and needs no Git tag or signing keys. Use a new or empty output
directory; existing archives are never overwritten. `-Version` labels the ZIP
filenames; assembly and MelonMod versions remain defined by their source.

Install the client ZIP beside `SCPSL.exe` and the server ZIP into the LabAPI root,
as described in the [main README](../README.md). Client directories are `Mods`,
`Plugins`, `UserLibs`, and `UserData`. The client ZIP includes the checked-in
logo/background pack from `release/assets/skins/`, installed as
`UserData/Anomaly/ContentPacks/personal/skins/pack.json`, with its explicit
`Assets` index and bundle under `assets/personal-skins.index.json` and
`assets/personal-skins.bundle`. Only the generated descriptor and selected pair
are included; authoring logs, Unity manifests, smoke plugins, other local packs,
and personal profiles are excluded.

The minimum additional third-party runtime libraries for the current supported
game/loader profile are:

| ZIP | Bundled third-party DLLs | Runtime support already supplied by the host |
|---|---|---|
| Client | `BouncyCastle.Cryptography.dll`, `AssetsTools.NET.dll`, `YamlDotNet.dll`, `Anomaly.MoonSharp.Interpreter.dll` in `UserLibs/` | MelonLoader 0.7.3 supplies HarmonyX, MonoMod, Newtonsoft.Json, and Il2CppInterop; the game supplies Unity/generated interop; .NET 6 supplies framework assemblies. |
| Server | `0Harmony.dll` (Lib.Harmony 2.2.2) in `dependencies/global/` | SCP:SL 14.2.7 supplies Bouncy Castle 2.0.0.0 and game/Unity assemblies; LabAPI supplies the plugin host; the server supplies its framework runtime. |

The client also carries six Anomaly DLLs: Bootstrap in `Plugins/`, Client in
`Mods/`, and Api, Scripting, Shared, and AssetValidation in `UserLibs/`. The server carries
Server in `plugins/global/` and Shared in `dependencies/global/`. Both installation
ZIPs include `LICENSE`, `NOTICE`, and `THIRD_PARTY_DEPENDENCIES.md`. The client also
includes its skin bundle/index pair, asset guide, AssetsTools.NET notice, and the
private MoonSharp fork's BSD-3-Clause license and provenance under `licenses/`.
Server-provided client source remains disabled until the scripting runtime's
memory safeguards are qualified. Build tools, test libraries, NVorbis, and
raw-image decoders are unnecessary at runtime.

Extract the SDK ZIP into a separate developer directory. It includes the client
and server API DLLs, Shared and its public Bouncy Castle compile dependency,
adjacent XML IntelliSense docs, shipped API baselines, reference metadata,
developer guides, four starter projects, and their supporting sources. The server
starter includes `samples/HelloAnomalyServer/ContentPacks/example/panel/pack.json`,
`scripts/main.lua` and `scripts/messages.json` beneath that pack directory;
retain the complete directory beside its project when building.
Scripting guides are under `Anomaly.Client.Scripting/`: start with `README.md`,
`SCRIPT-PACKS.md`, `MESSAGING-DESIGN.md`, and `CAPABILITIES.md`; lifecycle and
allocation-audit companions are also included. Source-relative guide paths and
adjacent script exports are preserved so their links work in the extracted SDK.
The server author guide is `Anomaly.Server/MODDING.md`.
Its `reference/` directory also contains the approved script surface baseline,
`Anomaly.Client.Scripting.ScriptAPI.json`, and the generated LuaLS definitions
`Anomaly.Client.Api.d.lua`. These describe the same approved types, members,
and queued events as the runtime catalog. SDK documentation and license files
stay in that archive, separate from game installation files. Its entries come
from the complete `anomaly-sdk` definition in `package-layout.json`.

The starter's `EnableScriptPack` defaults to `false`. Do not enable it for
normal remote connections: remote script offers fail closed before content or
execution approval while `MemoryHardeningQualified` / `RemoteExecutionEnabled`
are false. Consent cannot override this gate. The embedded pack is an authoring
and development example, not a claim that remote Lua is ready; verified local-host
sources use the guide's separate local owner directory and execution path.

The script uses the entries in `package-layout.json` as its allowlist and audits the emitted
assembly references against packaged DLLs and the actual resolved host inputs.
The default skin pair is declared in that layout for the full release pipeline
as well; `-SkinIndexPath` substitutes a different skin pair for the local command.
It validates the skin index, bundle length/hash, and both texture roots with the
production bundle validator, then checks every ZIP entry's path, length, and
SHA-256. All three ZIPs are validated, and the SDK's four starters and its
positive/negative compile-dependency probes are built using the same host
references as the runtime projects, before any artifacts reach the output
directory. The staged server starter's emitted PE metadata must contain all three
exact embedded names `HelloAnomalyServer.Panel/pack.json`,
`HelloAnomalyServer.Panel/scripts/main.lua` and
`HelloAnomalyServer.Panel/scripts/messages.json`; a successful build alone is
not enough. `DeployLocal=false` applies throughout.
This command creates unsigned local packages; the complete release gate below
provides the additional core tests, reproducibility, and signing evidence for
publication.

After building the default skin package, run the focused regression checks:

```powershell
./release/Test-RuntimeRelease.ps1 -Version 1.0.0
```

These require the exact declared runtime files, including the DLL/install paths,
skin pair, and license notices, verify every archive's source hashes and `SHA256SUMS`, reject
corrupt or incomplete skin packs, and prove an existing output directory is
rejected without changing its artifacts. They do not start the game or server.

Building runtime releases from a source checkout requires the .NET 8 SDK (or a
newer SDK plus the .NET 8 runtime), configured local game/loader references in
`Directory.Build.props`, and the restored private MoonSharp fork. `third_party/`
is ignored and a fresh clone does not contain that fork. Obtain the upstream
MoonSharp commit `fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` separately, then from the
full source checkout run:

```powershell
pwsh -File tools/MoonSharpHardening/Restore-MoonSharp.ps1 `
  -UpstreamSourceDirectory 'X:\Sources\moonsharp\src\MoonSharp.Interpreter'
```

The restore verifies pinned source hashes, applies the tracked hardening patch,
and refuses an existing destination; it does not fetch upstream sources or
replace an existing local fork. See the [restore prerequisites](../tools/MoonSharpHardening/README.md).
The extracted SDK includes that guidance and the fork's limits for reference,
not the runtime source tree or restore toolchain. Building its starter plugins
does not require restoring MoonSharp. Override host reference locations or supply
a different exported skin pair when needed:

```powershell
./release/Build-RuntimeRelease.ps1 -Version 1.0.0 `
  -OutputDirectory artifacts/my-runtime-release `
  -ClientRoot 'E:\Games\SCP Secret Laboratory - Modded' `
  -ServerGameDirectory 'E:\Server\server' `
  -ServerReferenceDirectory 'E:\Server\server\SCPSL_Data\Managed' `
  -LabApiDirectory 'E:\Server\server\SCPSL_Data\Managed' `
  -SkinIndexPath 'E:\Packs\skins\personal-skins.index.json'
```

The skin index must have exactly `game/skin/logo` and `game/skin/background`
Texture2D roots, with its named `.bundle` beside it. An invalid or missing pack
fails the command. Reference overrides apply to both builds and dependency
capture; missing references report their configured path.

Run the complete unsigned release gate from the repository root:

```powershell
./release/Invoke-CompleteReleaseGate.ps1 -Version 1.0.0
```

This command runs the private-key marker scan, explicit compile-list check,
Release/x64 warning-as-error build, every core test, shipped API baseline,
release-signing tool tests, runtime package closure tests, and staged SDK sample
compilation. It then performs a clean second build and requires the complete
unsigned release output to be byte-for-byte identical. Deploy-local is disabled
throughout. Controlled CI supplies the client/server/LabAPI reference-root
properties documented in `Directory.Build.props`; missing inputs fail with the
exact property and path.

Only after the two unsigned outputs compare byte-for-byte does the gate add an
`evidence/` directory to the primary output and finalize schema-v3
`release-receipt.json`. The receipt binds every per-gate command log and TRX
result by relative path, size, and SHA-256; records the pass state and UTC
interval for each gate; and records the exact .NET runtimes/SDK, PowerShell,
Git, and ripgrep versions used. Source commit, dependency-inventory SHA-256,
and package archive SHA-256 values remain in the same receipt. The unsigned
gate explicitly records `signing.state=unsigned`, a null `keyId`, and an empty
`trustedKeyIds` list. `Test-ReleaseEvidence.ps1` independently re-verifies the
receipt and every retained evidence file, then proves a modified copy of a
command log is rejected before the gate reports success.

Both the complete gate and the lower-level packager require a clean worktree
and require `SourceCommit` to equal the checked-out commit. This prevents a
receipt from attributing uncommitted source bytes to an unrelated Git object.
Finish and review the implementation, commit it deliberately, and then create
the release evidence from that clean checkout.

`Build-Release.ps1` is the lower-level build/package/signing command. It builds
unless `-SkipBuild` is supplied, captures and hashes resolved inputs, stages the
declared payloads, emits canonically ordered commit-timestamped archives, and
verifies the result. It deliberately does not claim to run the complete test,
API, secret, or two-build reproducibility gate; use
`Invoke-CompleteReleaseGate.ps1` before signing or publishing.

`release/package-layout.json` is the reviewed closure policy. Packaged DLLs are
verified from their PE assembly metadata. Framework exclusions are exact names
provided by the declared .NET runtime. Host exclusions are exact names tied to
MelonLoader 0.7.3 or the SCP:SL 14.2.7 client/server and must be observed in the
resolved MSBuild inputs. The client archive carries Bouncy Castle; the server
uses the SCP:SL 14.2.7 game's Managed copy, whose assembly version is pinned
in the closure declaration. The server carries Lib.Harmony 2.2.2. The client
uses MelonLoader 0.7.3's HarmonyX 2.10.2 and Newtonsoft.Json 13.0.4 instead
of redistributing competing copies. The SCP:SL, Unity, IL2CPP interop,
MelonLoader, LabAPI, and
CLR/framework assemblies remain host-provided and are not redistributed.

The local and complete release pipelines' client, server, and SDK archives include the project `LICENSE`, `NOTICE`,
and the exact `THIRD_PARTY_DEPENDENCIES.md` whose SHA-256 is bound into the
dependency inventory. The declaration contains an assembly-by-assembly map for
all redistributed third-party runtime DLLs. Mono.Cecil remains a build-tool
dependency and is not carried by the client archive.

The developer SDK includes `BouncyCastle.Cryptography.dll` beside
`Anomaly.Shared.dll` under `lib/net48` because `AuthCrypto` exposes Bouncy
Castle types in its public signatures. Its reference metadata records this
compile dependency and declares the separate, non-redistributed SCP:SL client
interop profile. CI compiles a staged-SDK probe both with and without that DLL
to prove the dependency is necessary and present.

Signing requires all three external operator inputs:

```powershell
./release/Build-Release.ps1 -Version 1.0.0 `
  -OutputDirectory artifacts/release-1.0.0-signed `
  -SigningKeyPath X:\controlled\release-private.pem `
  -PublicKeyPath X:\controlled\release-public.pem `
  -KeyId production-2026
```

Keys must be ECDSA P-256. No production private key belongs in this repository
or a normal CI worker. Use a new empty output directory for the signing pass;
the lower-level packager intentionally refuses to overwrite the unsigned gate
evidence. The trusted public key and `keyId` must be added to the
launcher's embedded trust store before publishing. The distribution endpoint
must return the generated client envelope (`manifestBase64`,
`signatureBase64`, and `dependencyInventoryBase64`) and every artifact named by
the signed `anomaly-client` manifest, mapped to its launcher install target.
The mandatory launcher identity remains `target=client-runtime` and
`packageId=releaseId=anomaly-client`; adding dependency artifacts does not alter
the schema-v2 signed-manifest contract. Each archive hash is recorded in the
dependency inventory, whose SHA-256 is bound by the signed manifest, while the
manifest also signs every post-extraction artifact hash and size.
After all signatures and the official client envelope verify,
`Build-Release.ps1` changes the receipt signing state to `signed`, records the
verified `keyId`, and binds the public manifests, signatures, and envelope by
size and SHA-256. Neither key path nor any private/public key bytes are written
to the receipt. This signing-evidence update is intentionally outside the
unsigned reproducibility comparison.
