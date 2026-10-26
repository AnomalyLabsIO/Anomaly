# MoonSharp 3 source-build recheck

The supplied `E:\Projects\SCPSL\SCriPt\SCriPt\libs\MoonSharp.Interpreter.dll` is the correct maintained-source binary to test. Its informational version is **3.0.0-beta.1.16+fbd03678d5c0f731d731d099211c7f4a3a1ae8b7**, and that revision matched official upstream `master` when checked. This is a newer source build, not the decade-old NuGet 2.0.0 package. The earlier comparison established only the 2.0.0 package's behavior; this recheck tests the actual current DLL.

## Binary identity and provenance

| Property | Supplied / actually loaded DLL |
|---|---|
| Assembly and file version | `3.0.0.0` |
| Informational version | `3.0.0-beta.1.16+fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` |
| Target framework | `.NETFramework,Version=v4.5` |
| SHA-256 | `E7486C37ACFB88159E62FB6F6B651CBA271D2F726F35BFEFDA0564FABCB10265` |
| Module version ID | `16a6245c-18b8-44a9-9a44-7f51e67a5ec8` |
| Public-key token | `921e73ce94aa17f8` |

The assembly's build metadata, the SCriPt build documentation, the existing official-upstream checkout, and its matching `bin/Release/net45` output agree. All 248 tracked interpreter `.cs`/`.csproj` files in that checkout matched the recorded commit after line-ending normalization. The exact file copied into the prototype has the same SHA-256 as the supplied DLL; its loaded assembly identity/hash are written into the report. The original DLL was not modified.

No fresh source download/build was needed: the supplied binary was current and compiled against the prototype without any MoonSharp adapter change. It is a .NET Framework 4.5 build, so these results specifically establish the tested paths under the standalone .NET 6 host; they do not establish Unity/IL2CPP runtime compatibility.

## Fresh results against both versions

The same 32 fixture templates, shared object API, source limits, coroutine scheduling and process safeguards were run on both DLLs using the current harness. **All 32 outcome classifications were identical.** Each version also passed all 6 host contract checks. Parameterized witness paths/ports and resulting source hashes differ between report directories; this is a comparison of the same fixture templates and limits, not byte-identical host paths.

| Probe / property | NuGet 2.0.0 | Supplied current 3.0 source build |
|---|---|---|
| Approved object API, bounded arguments/quotas and rejected capabilities | Same expected behavior | Same expected behavior |
| Ordinary infinite loop / loop inside `pcall` | `CpuLimit` | `CpuLimit` |
| Reentrant `string.gsub` infinite callback | `ProcessTimeout`, about 2 seconds | `ProcessTimeout`, about 2 seconds |
| One 8 MiB string under the proposed 4 MiB guest quota | `Completed` | `Completed` |
| Loop swallowing allocation errors | `ProcessTimeout` | `ProcessTimeout` |
| Incrementally growing string table | `WorkerMemoryFailure` | `WorkerMemoryFailure` |
| Actual filesystem/process/CLR/module/HTTP attempts | Denied | Denied |
| Exact file sentinel checks | 32/32 intact | 32/32 intact |
| Script loopback connections | 0 | 0 |
| Fixture expectations, including known limitations | 32/32 matched | 32/32 matched |

`ProcessTimeout` and `WorkerMemoryFailure` are external process safeguards, not successful interpreter resource limits. The expected-results score includes these limitations. Successful allocation above 4 MiB demonstrates that this adapter has no enforced guest heap quota on either version. Neither run proves the absence of unknown interpreter escapes. Single-run timing/allocation differences are not a performance regression or improvement claim, especially given the different target-framework builds.

Artifacts:

- [Fresh 2.0.0 report](../../artifacts/script-sandbox/moonsharp2-recheck/comparison.md), [JSON](../../artifacts/script-sandbox/moonsharp2-recheck/comparison.json).
- [Current local 3.0 report](../../artifacts/script-sandbox/moonsharp3-local/comparison.md), [JSON](../../artifacts/script-sandbox/moonsharp3-local/comparison.json).
- [Version outcome/identity comparison](../../artifacts/script-sandbox/moonsharp-version-delta.json): zero outcome differences.

## Why the resource results remain the same

A source comparison of official 2.0 tag commit `3154416e535ab96c0d52bf12e3e472985a1532f4` against current `fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` supports the observations:

- [`ScriptOptions.cs`](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/ScriptOptions.cs) is unchanged ignoring whitespace and adds no per-script heap or execution-time setting.
- [`Processor_InstructionLoop.cs`](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs) still gates automatic yielding on a positive counter, a yieldable processor and a non-main coroutine. [`Processor.Call`](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor.cs) still disables yielding during nested calls.
- [`StringModule.cs`](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/CoreLib/StringModule.cs) still uses ordinary managed allocations for `string.rep`, without per-VM allocation accounting.
- [`CoreModules.cs`](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Modules/CoreModules.cs) is unchanged ignoring whitespace. The prototype's explicit restricted surface remains necessary.

Current source includes fixes and other development, including changes around stack/coroutine recycling and callback exception handling. These findings concern the inspected resource-control paths and the tested sandbox, not a claim that current MoonSharp is generally equivalent to 2.0.0.

The earlier recommendation therefore still holds for the tested malicious-server resource threat: Luau's configured VM allocation/CPU controls contained the original adversarial fixtures, while both MoonSharp versions required process safeguards for some cases. MoonSharp's suitability for a capability-restricted game API is unchanged; reliable in-process CPU/heap containment is the unresolved part.

## Reproduce

A separate project reads the chosen DLL through a direct reference, copies it to its own output directory, and compiles the same harness sources. It has no MoonSharp NuGet reference and uses separate `obj`/`bin` directories; it cannot silently fall back to the 2.0.0 package. Both projects' Release builds passed with 0 warnings/errors.

```powershell
pwsh -File tools/ScriptSandboxPrototype/Run-Comparison.ps1 -SkipNativeBuild -Runtime moonsharp -MoonSharpAssemblyPath 'E:\Projects\SCPSL\SCriPt\SCriPt\libs\MoonSharp.Interpreter.dll' -OutputDirectory artifacts/script-sandbox/moonsharp3-local

pwsh -File tools/ScriptSandboxPrototype/Run-Comparison.ps1 -SkipNativeBuild -Runtime moonsharp -OutputDirectory artifacts/script-sandbox/moonsharp2-recheck
```

Only prototype tooling/reporting changed. The 50 existing asset validation/client asset/content/shared asset-contract files remained SHA-256 identical to the original baseline. No production project, wire protocol, content validation, game installation or SCriPt source/binary was changed, and no game/server was launched.
