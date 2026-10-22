# MelonLoader 0.7.3 reference and capability review

Reviewed on 2026-09-26 against the installed binaries and the tagged upstream
source. Anomaly now builds Client, Client.Api, and Bootstrap against
`E:\Steam\steamapps\common\SCP Secret Laboratory - Copy\MelonLoader`.
`Directory.Build.props` selects both `net6` and `Il2CppAssemblies` from that
installation. Existing MSBuild overrides remain available. Server reference
paths, target frameworks, deployment destinations, and wire formats are unchanged.

The installed loader identifies itself as
`0.7.3+982ed9981ac5b0619648a2e79bcc02fd8861db33`, matching upstream's release
commit. Its existing log identifies SCP:SL 14.2.7, Unity 6000.0.43f1, x64, and
.NET 6.0.36. This review did not start the game or regenerate its interop cache.

| Input | Previous reference installation | New reference installation |
|---|---|---|
| MelonLoader / NativeHost | 0.7.2 | 0.7.3 |
| Il2CppInterop Runtime, Common, Generator, HarmonySupport | 1.5.1, commit `dbaf825` | 1.5.1-ci.845, commit `f03c8f4` |
| AsmResolver family | 6.0.0-beta.3 | 6.0.0-beta.5 |
| Unity IL2CPP AssetBundle and ImageConversion helpers | 0.7.2 | 0.7.3 |
| IndexRange | 1.1.0 | 1.1.1 |
| Microsoft.Bcl.Memory | 9.0.0 | 9.0.14 |
| HarmonyX | 2.10.2 | 2.10.2, identical SHA-256 |
| Newtonsoft.Json | 13.0.4 | 13.0.4, identical SHA-256 |
| MonoMod.RuntimeDetour / MonoMod.Utils | 22.7.31.1 | Same versions and bytes |
| Mono.Cecil / Tomlet | 0.11.6 / 6.2.0 | Same versions and bytes |
| Generated game/Unity DLLs | 109 DLLs | Same 109 filenames; every DLL hash differs |

These values come from the two local installations. The interop assembly version
is still `1.5.1.0`; its informational version and hash identify the newer build.
Anomaly continues to use loader-provided Harmony and JSON assemblies without
packaging competing client copies. The release policy now requires MelonLoader
assembly version `0.7.3.0` and records the matching interop provider.

**Callable APIs and Unity capabilities**

Comparing the installed public signatures found two additions in `MelonLoader.dll`:
`MelonUtils.TryPatchAll(Harmony, Assembly, bool)` and
`MelonUtils.TryPatchAll(Harmony, Type, bool)`. Both return `List<MethodInfo>` for
generated patches and explicitly accept `allowUnannotatedType`. Existing
two-argument overloads remain. The assembly overload defaults to annotated
types; the type overload allows an unannotated type. The type-level helper
catches failures and can return null, so its result alone is not a complete hook
health check. These overloads are accessible through Anomaly.Client's existing
MelonLoader and Harmony references.
[Tagged implementation](https://github.com/LavaGang/MelonLoader/blob/v0.7.3/MelonLoader/MelonUtils.cs).

| Capability | What 0.7.3 changes | Relevance to Anomaly |
|---|---|---|
| Unity 6 AssetBundles | Corrects injected internal-call signatures, managed spans, GC handles, and native object pointers in `Il2CppAssetBundleManager` and `Il2CppAssetBundle`. | The loader helper becomes a candidate for file/memory/stream loading, asynchronous requests, and asset enumeration. Those APIs already existed; Unity 6 compatibility is the upgrade. |
| Unity 6 image conversion | Corrects `Il2CppImageConversionManager.LoadImage` and PNG/JPG/TGA/EXR encoding for Unity 6's pointer and buffer marshalling. | Useful for future image import, texture export, and diagnostic captures. Current Anomaly resources primarily use AssetBundles and RGBA data, so the upgrade alone adds no new Anomaly image API. |
| Unity rectangle preferences | Adds Tomlet mappings for `Rect` and, in IL2CPP, `RectInt`, using x/y/width/height values. | Ordinary mods can store UI bounds directly through MelonPreferences. Anomaly's `ClientConfig` still stores JSON strings, preserving its existing format. |
| Preference notifications | Raises load/save notifications at the preference-file operation, fixing missed callbacks. | Existing MelonPreferences consumers receive the loader fix. It does not remove the need for Anomaly's save-error and retry handling. |

The bundle and image fixes were tested upstream on Unity 2022.3.62f2 and
6000.3.10f1. SCP:SL uses 6000.0.43f1, so those results do not establish this
game's runtime behavior. Sources: [bundle fix](https://github.com/LavaGang/MelonLoader/pull/1122),
[image fix](https://github.com/LavaGang/MelonLoader/pull/1144),
[rectangle mappings](https://github.com/LavaGang/MelonLoader/blob/v0.7.3/Dependencies/SupportModules/UnityMappers.cs),
and [preference I/O](https://github.com/LavaGang/MelonLoader/blob/v0.7.3/MelonLoader/Preferences/IO/File.cs).

The optional helper assemblies are
`$(AnomalyClientMelonLibDir)\UnityEngine.Il2CppAssetBundleManager.dll` and
`$(AnomalyClientMelonLibDir)\UnityEngine.Il2CppImageConversionManager.dll`.
Feature work that directly calls them should add a `Reference` with
`Private="false"`; neither is currently an emitted Anomaly dependency, so this
update does not add unused references or redistribute them.

Anomaly's `Anomaly.Client/Assets/NativeAssetBundle.cs` already handles this
player's stripped AssetBundle type, synchronous injected calls, object lifetime,
and asset retention. Replacing it with the loader helper is separate feature
work. First compare load, use, `Resources.UnloadUnusedAssets`, and unload behavior
on this exact game profile. The loader helper has its own native-pointer handling;
the version upgrade is not evidence that it preserves Anomaly's lifetime rules.

**Included Il2CppInterop changes**

The exact old-to-new [commit comparison](https://github.com/BepInEx/Il2CppInterop/compare/dbaf825aab891ff1e1627bea691bde0a81d1ad98...f03c8f4ae507d47ea814f3d11d1ec6b0391c1576)
contains ten commits. Beyond the AsmResolver adaptation and license clarification:

- Unity 6 and metadata-v39 generic-method hooks are supported, with a related
  Linux alignment correction. These improve the underlying type-injection and
  generic interop paths. [Unity 6 change](https://github.com/BepInEx/Il2CppInterop/pull/255).
- Nested class names are constructed correctly during injection.
  [Nested-type fix](https://github.com/BepInEx/Il2CppInterop/pull/233).
- Delegate return conversion now uses the managed method's return type when
  deciding how to marshal strings and reference objects.
  [Delegate fix](https://github.com/BepInEx/Il2CppInterop/pull/256).
- Harmony argument conversion preserves the complete `Il2CppSystem.Nullable<T>`
  value, including `HasValue`.
  [Nullable fix](https://github.com/BepInEx/Il2CppInterop/pull/259).
- Generated by-reference value-type calls unbox the object before passing its
  value pointer. This is a generation-time fix, so matching generated assemblies
  matter as well as the runtime DLL.
  [Generator fix](https://github.com/BepInEx/Il2CppInterop/pull/243).
- A finalizer hook and associated object-pool re-registration are removed.
  [Finalizer change](https://github.com/BepInEx/Il2CppInterop/pull/247).
- Managed calls to Harmony-patched interop methods take a direct managed detour,
  avoiding the redundant managed/native/managed route.
  [Detour change](https://github.com/BepInEx/Il2CppInterop/pull/237).

**A concrete compatibility concern:** the last change, commit `769f080`, is in
the installed `f03c8f4` build. Upstream subsequently reverted it in
[PR 271](https://github.com/BepInEx/Il2CppInterop/pull/271), merged June 20, 2026,
because copying an already-detoured method body can break multiple Harmony
patches on one target. That later fix is outside this bundled version. The local
unit tests do not execute the real IL2CPP patch engine. Test overlapping Anomaly
and companion-mod patches before treating a successful startup as compatibility
qualification. This is an upstream defect report, not a locally reproduced
Anomaly failure; no host DLL substitution was performed.

The installed Runtime and Common assemblies have no public-signature changes
in the local comparison. Their improvements are primarily implementation fixes.
No game FPS or startup speed improvement was measured in this pass.

**Loader, launcher, and platform changes**

Portable .NET discovery supports the executable's `dotnet` directory, the
configured base directory's `dotnet`, and
`<base>/MelonLoader/Dependencies/dotnet`. A configured HostFXR override is tried
first, then those portable locations, then system discovery, the Windows
installer path, and a portable download fallback. This gives the launcher a
supported route to a private runtime. The supplied installation still runs .NET
6; it does not grant access to .NET 8/10 target frameworks merely because some
bundled dependency version numbers are newer. The existing log used system
.NET 6.0.36, so portable startup was not exercised here.
[Tagged runtime selection](https://github.com/LavaGang/MelonLoader/blob/v0.7.3/MelonLoader.Bootstrap/RuntimeHandlers/Dotnet/DotnetHandler.cs).

The remaining release changes cover:

- Wine/Proton delegate-hook crashes; Linux startup, native hooks, and cross-builds;
  macOS compilation, bootstrap interception, and an installation script.
- NativeHook compatibility and recursive-trampoline handling; injection setup;
  full internal-call signatures and skipping unsupported pinnable-reference shims.
- Automatic Harmony type selection and forced resolution of the supplied Harmony DLL.
- Loader.cfg value preservation, missing-option recreation, debug defaults, and
  duplicate command-line options.
- Console-title/debug-marker behavior, warning/error identifiers, debug-only type
  registration logging, and pinned-delegate warnings.
- NuGet dependency metadata and the AsmResolver/interop dependency updates.

These are primarily loader behavior fixes. They do not require adding unrelated
packages to Anomaly, and the native Linux/macOS changes do not establish a new
SCP:SL platform target. [Official release notes](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3).

**Verification of this reference migration**

- `dotnet build Anomaly.Client/Anomaly.Client.csproj -c Debug -p:DeployLocal=false`:
  passed, zero warnings/errors; includes Client.Api and Bootstrap.
- `dotnet build Anomaly.slnx -c Release -p:DeployLocal=false -m:1 -warnaserror`:
  passed, zero warnings/errors; includes the client mod/module samples and server.
- `dotnet test Anomaly.slnx -c Debug -p:DeployLocal=false -m:1`:
  567 passed: Client API 158, Bootstrap 6, AssetValidation 61, Server 40, Shared 302.
- `dotnet run --project tools/PublicApiBaseline/PublicApiBaseline.csproj -- Anomaly.Client.Api/bin/Debug/net6.0/Anomaly.Client.Api.dll Anomaly.Client.Api/PublicAPI.Shipped.txt`:
  matched the shipped contract.
- MSBuild's `WriteAnomalyResolvedReferences` showed Client's 112, Client.Api's
  111, and Bootstrap's one loader-tree references all resolving beneath the new
  installation, with no old loader root mixed in.
- Metadata inspection confirmed that the private preference members used by
  `ModSettingsSaveScheduler` still exist; tagged source retains the error latch
  and watcher behavior. This is structural validation, not a live disk-failure test.

Local logs, dependency hashes, API comparisons, and resolved reference lists are
under the ignored `artifacts/melonloader-0.7.3/` directory. No new source compile
includes were needed. No deployment, game launch, live Proton run, signing,
publication, or commit was performed. Existing user edits were preserved.
