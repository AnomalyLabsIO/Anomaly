# Third-party dependency and license inventory

This is the reviewed direct-dependency inventory for public-v1 release
artifacts. The signed release manifest binds this file by SHA-256. NuGet restore
assets remain the machine-readable inventory for transitive build/test
dependencies; release engineering must archive those assets with each build.

| Dependency | Pinned version | Scope | License | Upstream |
|---|---:|---|---|---|
| MelonLoader | 0.7.2 production / 0.7.3 candidate | Client runtime/bootstrap | Apache-2.0 | https://github.com/LavaGang/MelonLoader |
| HarmonyX | 2.16.0 | Client patching | MIT | https://github.com/BepInEx/HarmonyX |
| Harmony | 2.10.2 runtime assembly | Client/server patching | MIT | https://github.com/pardeike/Harmony |
| Newtonsoft.Json | 13.0.5-beta1 | Client/API serialization | MIT | https://github.com/JamesNK/Newtonsoft.Json |
| BigGustave | 1.0.3 | Managed PNG decode | Unlicense | https://github.com/EliotJones/BigGustave |
| NVorbis | 0.10.5 | Managed Ogg Vorbis decode | MIT | https://github.com/NVorbis/NVorbis |
| YamlDotNet | 11.0.1 | Client localization | MIT | https://github.com/aaubry/YamlDotNet |
| Bouncy Castle C# | game/runtime-provided assembly | Client/server crypto | Bouncy Castle license (MIT-style) | https://www.bouncycastle.org/csharp/ |
| LabAPI | runtime-provided | Server plugin host | LGPL-3.0 | https://github.com/northwood-studios/LabAPI |
| Mono.Cecil | 0.11.6 | Release API-baseline tool | MIT | https://github.com/jbevain/cecil |
| xUnit.net | 2.7.0 | Tests only | Apache-2.0 | https://github.com/xunit/xunit |
| xUnit VS runner | 2.5.7 | Tests only | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |
| Microsoft.NET.Test.Sdk | 17.9.0 | Tests only | MIT | https://github.com/microsoft/vstest |

The SCP: Secret Laboratory game assemblies, Unity runtime, and generated
IL2CPP interop assemblies are external build/runtime inputs and are not
redistributed as Anomaly framework source dependencies. Their exact hashes and
the installer source/build catalog must be retained with controlled release
evidence.
