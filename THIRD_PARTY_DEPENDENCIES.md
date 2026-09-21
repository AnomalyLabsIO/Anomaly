# Third-party dependency and license inventory

This is the reviewed direct-dependency inventory for public-v1 release
artifacts. The signed release manifest binds this file by SHA-256. NuGet restore
assets remain the machine-readable inventory for transitive build/test
dependencies; release engineering must archive those assets with each build.

| Dependency | Pinned version | Scope | License | Upstream |
|---|---:|---|---|---|
| MelonLoader | 0.7.2 production / 0.7.3 candidate | Client runtime/bootstrap | Apache-2.0 | https://github.com/LavaGang/MelonLoader |
| HarmonyX | 2.16.0 | Client patching; packaged client `0Harmony.dll` | MIT | https://github.com/BepInEx/HarmonyX |
| Harmony | 2.10.2 deployed input (assembly 2.2.2.0) | Server patching; packaged server `0Harmony.dll` | MIT | https://github.com/pardeike/Harmony |
| Newtonsoft.Json | 13.0.5-beta1 | Client/API serialization | MIT | https://github.com/JamesNK/Newtonsoft.Json |
| BigGustave | 1.0.3 | Managed PNG decode | Unlicense | https://github.com/EliotJones/BigGustave |
| NVorbis | 0.10.5 | Managed Ogg Vorbis decode | MIT | https://github.com/NVorbis/NVorbis |
| YamlDotNet | 11.0.1 | Client localization | MIT | https://github.com/aaubry/YamlDotNet |
| Bouncy Castle C# | assembly 2.0.0.0 from the SCP:SL 14.2.7 reference input | Packaged client/server runtime and SDK compile closure | Bouncy Castle license (MIT-style) | https://www.bouncycastle.org/csharp/ |
| System.Numerics.Vectors | 4.4.0 | Server runtime compatibility closure | MIT | https://www.nuget.org/packages/System.Numerics.Vectors/4.4.0 |
| System.Threading.Tasks.Extensions | 4.5.4 | Server runtime compatibility closure | MIT | https://www.nuget.org/packages/System.Threading.Tasks.Extensions/4.5.4 |
| LabAPI | runtime-provided | Server plugin host | LGPL-3.0 | https://github.com/northwood-studios/LabAPI |
| Mono.Cecil | 0.11.6 | Packaged client HarmonyX/MonoMod closure; release tools | MIT | https://github.com/jbevain/cecil |
| MonoMod.Backports | 1.1.2 | Packaged client HarmonyX/MonoMod closure | MIT | https://github.com/MonoMod/MonoMod |
| MonoMod.Core | 1.3.3 | Packaged client HarmonyX/MonoMod closure, including `MonoMod.Iced.dll` | MIT | https://github.com/MonoMod/MonoMod |
| MonoMod.ILHelpers | 1.1.0 | Packaged client HarmonyX/MonoMod closure | MIT | https://github.com/MonoMod/MonoMod |
| MonoMod.RuntimeDetour | 25.3.3 | Packaged client HarmonyX runtime detouring | MIT | https://github.com/MonoMod/MonoMod |
| MonoMod.Utils | 25.0.11 | Packaged client HarmonyX/MonoMod closure | MIT | https://github.com/MonoMod/MonoMod |
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
| Client | `UserLibs/0Harmony.dll` | HarmonyX |
| Client | `UserLibs/BigGustave.dll` | BigGustave |
| Client | `UserLibs/Mono.Cecil.dll` | Mono.Cecil |
| Client | `UserLibs/Mono.Cecil.Mdb.dll` | Mono.Cecil |
| Client | `UserLibs/Mono.Cecil.Pdb.dll` | Mono.Cecil |
| Client | `UserLibs/Mono.Cecil.Rocks.dll` | Mono.Cecil |
| Client | `UserLibs/MonoMod.Backports.dll` | MonoMod.Backports |
| Client | `UserLibs/MonoMod.Core.dll` | MonoMod.Core |
| Client | `UserLibs/MonoMod.Iced.dll` | MonoMod.Core |
| Client | `UserLibs/MonoMod.ILHelpers.dll` | MonoMod.ILHelpers |
| Client | `UserLibs/MonoMod.RuntimeDetour.dll` | MonoMod.RuntimeDetour |
| Client | `UserLibs/MonoMod.Utils.dll` | MonoMod.Utils |
| Client | `UserLibs/Newtonsoft.Json.dll` | Newtonsoft.Json |
| Client | `UserLibs/NVorbis.dll` | NVorbis |
| Client | `UserLibs/YamlDotNet.dll` | YamlDotNet |
| Server | `dependencies/global/BouncyCastle.Cryptography.dll` | Bouncy Castle C# |
| Server | `dependencies/global/0Harmony.dll` | Harmony |
| Server | `dependencies/global/System.Numerics.Vectors.dll` | System.Numerics.Vectors |
| Server | `dependencies/global/System.Threading.Tasks.Extensions.dll` | System.Threading.Tasks.Extensions |

The SCP: Secret Laboratory game assemblies, Unity runtime, and generated
IL2CPP interop assemblies are external build/runtime inputs and are not
redistributed as Anomaly framework source dependencies. Their exact hashes and
the installer source/build catalog must be retained with controlled release
evidence.
