# Third-party dependency and license inventory

This is the reviewed direct-dependency inventory for public-v1 release
artifacts. The signed release manifest binds this file by SHA-256. NuGet restore
assets remain the machine-readable inventory for transitive build/test
dependencies; release engineering must archive those assets with each build.

| Dependency | Pinned version | Scope | License | Upstream |
|---|---:|---|---|---|
| MelonLoader | 0.7.3 | Client runtime/bootstrap | Apache-2.0 | https://github.com/LavaGang/MelonLoader |
| Il2CppInterop | 1.5.1-ci.845 (`f03c8f4ae507d47ea814f3d11d1ec6b0391c1576`; assembly 1.5.1.0) | Client generated interop/runtime; provided by MelonLoader 0.7.3 | LGPL-3.0 | https://github.com/BepInEx/Il2CppInterop |
| HarmonyX | 2.10.2 | Client patching; provided by MelonLoader 0.7.3 | MIT | https://github.com/BepInEx/HarmonyX |
| MonoMod.RuntimeDetour / MonoMod.Utils | 22.7.31.1 | Bootstrap authentication-path monitoring; provided by MelonLoader 0.7.3 | MIT | https://github.com/MonoMod/MonoMod |
| Lib.Harmony | 2.2.2 (assembly 2.2.2.0) | Server patching; packaged server `0Harmony.dll` | MIT | https://github.com/pardeike/Harmony |
| Newtonsoft.Json | 13.0.4 | Client/API serialization; provided by MelonLoader 0.7.3 | MIT | https://github.com/JamesNK/Newtonsoft.Json |
| AssetsTools.NET | 3.0.4 | Client bounded AssetBundle metadata inspection; core parser only | MIT | https://github.com/nesrak1/AssetsTools.NET |
| MoonSharp (Anomaly fork) | `3.0.0-anomaly.4`, upstream `fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` | Client scripting support; privately named `Anomaly.MoonSharp.Interpreter.dll`, built for .NET 6 | BSD-3-Clause, including upstream KopiLua attribution | https://github.com/moonsharp-devs/moonsharp |
| NVorbis | 0.10.5 | Developer asset-catalog tool only; not shipped in the client | MIT | https://github.com/NVorbis/NVorbis |
| YamlDotNet | 11.0.1 | Client localization | MIT | https://github.com/aaubry/YamlDotNet |
| Bouncy Castle C# | assembly 2.0.0.0 from the SCP:SL 14.2.7 reference input | Packaged client runtime and SDK compile closure; server uses the game-provided Managed copy | Bouncy Castle license (MIT-style) | https://www.bouncycastle.org/csharp/ |
| LabAPI | runtime-provided | Server plugin host | LGPL-3.0 | https://github.com/northwood-studios/LabAPI |
| Mono.Cecil | 0.11.6 | Reference publicizer and public API baseline tools only | MIT | https://github.com/jbevain/cecil |
| xUnit.net | 2.7.0 | Tests only | Apache-2.0 | https://github.com/xunit/xunit |
| xUnit VS runner | 2.5.7 | Tests only | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |
| Microsoft.NET.Test.Sdk | 17.9.0 | Tests only | MIT | https://github.com/microsoft/vstest |

## Redistributed third-party assembly map

The release dependency inventory records the version, size, SHA-256, package
path, and assembly-reference edges for every DLL below. This table records its
reviewed component and license provenance; first-party Anomaly assemblies are
covered by the accompanying `LICENSE` and `NOTICE` files.

| Archive | Redistributed assembly | Component above |
|---|---|---|
| Client | `UserLibs/BouncyCastle.Cryptography.dll` | Bouncy Castle C# |
| Client | `UserLibs/AssetsTools.NET.dll` | AssetsTools.NET; full notice in `licenses/AssetsTools.NET.md` |
| Client | `UserLibs/Anomaly.MoonSharp.Interpreter.dll` | MoonSharp Anomaly fork; license in `licenses/MoonSharp.LICENSE`, source/modification provenance in `licenses/MoonSharp.md` |
| Client | `UserLibs/YamlDotNet.dll` | YamlDotNet |
| Server | `dependencies/global/0Harmony.dll` | Lib.Harmony |

The MoonSharp fork retains its upstream `MoonSharp.Interpreter` namespaces but
uses a private assembly identity to avoid binding to another mod's unmodified
MoonSharp DLL. The local source tree in `third_party/MoonSharp/` is ignored.
Pinned restore inputs and the reviewable patch live in `tools/MoonSharpHardening/`.
CPU controls and experimental reclaimable allocation quotas do not qualify
per-script memory containment. Verified bundle source can execute experimentally after
separate approval; memory qualification remains false. No remote debugger is
redistributed with this interpreter.

The SCP: Secret Laboratory game assemblies, Unity runtime, and generated
IL2CPP interop assemblies are external build/runtime inputs and are not
redistributed as Anomaly framework source dependencies. Their exact hashes and
the installer source/build catalog must be retained with controlled release
evidence.
