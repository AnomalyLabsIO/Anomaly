# Observed MoonSharp / Luau containment comparison

The completed standalone .NET 6 / Windows x64 run exercised 32 source fixtures on each engine: **64/64 documented expected outcomes**, plus **6 host contract checks**. Expected outcomes deliberately include MoonSharp limitations; this is not a claim that all 64 cases were contained by their interpreter. The machine-readable [full report](../../artifacts/script-sandbox/comparison/comparison.json) and [per-case table](../../artifacts/script-sandbox/comparison/comparison.md) retain diagnostics, source hashes, allocation observations and process outcomes. Run again using [README.md](README.md).

The supplied maintained-source **MoonSharp 3.0.0-beta.1.16** build was subsequently verified and rerun against a fresh 2.0.0 baseline: **all 32 outcome classifications were the same**, including both watchdog cases and the missing guest heap quota. See [MOONSHARP3.md](MOONSHARP3.md) for the exact binary/current upstream revision, fresh reports and source comparison. The table below retains the original 2.0.0/Luau evidence.

| Probe / property | MoonSharp 2.0.0 | Luau 0.743 |
|---|---|---|
| Approved object lifecycle | Completed | Completed |
| Forged/destroyed handles, bounds, UTF-8 labels, approved asset names, object/attempt quotas | Rejected as intended | Rejected as intended |
| Ordinary infinite loop and loop inside `pcall` | Host-owned coroutine slice limit stopped them | Native VM budget stopped them |
| Reentrant `string.gsub` callback containing an infinite loop | Failed to return; parent killed worker after about 2 seconds | `CpuLimit` inside the runtime; about 6 ms execution in this run |
| One 8 MiB string result under the proposed 4 MiB VM quota | Completed, demonstrating no guest heap quota | `MemoryLimit` before allocation |
| Repeated allocation failures swallowed by `pcall` | Parent killed worker after about 2 seconds | Sticky `MemoryLimit`, not catchable into successful execution |
| Incrementally growing string table | Worker allocation failed under process/GC safeguards | `MemoryLimit`; tracked live VM peak 3,972,887 bytes, below the 4,194,304-byte cap |
| Actual file read/write/delete, process, CLR and module-loader attempts | Denied | Denied |
| Actual loopback HTTP attempts | Denied; zero script connections | Denied; zero script connections |
| Bytecode/dump, oversized source and parser failures | Rejected | Rejected |

The 8 MiB MoonSharp string fixture allocated about 36.3 MiB cumulatively in the managed runtime, including VM setup and temporary allocations. That is an allocation delta, not a live guest-heap measurement. Luau uses native UTF-8 strings and separate allocator accounting; these numbers cannot be compared as equivalent heaps. Windows CPU accounting is coarse, and the native step hook is deliberately instrumented for containment, so the timing observations are not a throughput or game-performance ranking.

All **64 sentinel checks preserved exact bytes**, and there were **zero script loopback connections**. Every worker first successfully read that sentinel and connected to the same listener from trusted host code, so these results were not caused by the OS denying file/network access. A separate parent control verified the listener, and its continued availability was checked. Neither prototype provides an OS filesystem/network sandbox or proves absence of unknown VM/binding escapes.

An initially retained MoonSharp `package` table was detected by the forbidden-globals fixture. The adapter now applies a positive global allowlist after module registration; the final run passed this check. Actual file/network operations were already denied in that earlier run. No production asset policy was relaxed to resolve it.

## Recommendation

**Prefer Luau for the next prototype stage under the malicious-server threat model.** It enforced both CPU and configured VM allocation limits for these adversarial fixtures, including protected-call and reentrant cases. The native binding and its dependency update process remain part of the security boundary. The custom allocation cap covers requested live VM allocations, not the compiler, allocator overhead, managed callbacks or future Unity resources. Compilation and expensive host functions still need their own safeguards.

MoonSharp remains practical for controlled scripting with a small API, but the tested library callbacks can prevent its coroutine scheduler from regaining control, and there is no per-VM heap quota here. The process watchdog is what contained those failures. Replacing it with an in-process timer would not reproduce that protection. Stronger MoonSharp containment would require additional interpreter work or execution in a properly OS-restricted helper, with a validated command channel back to the client.

Keep the language-independent object API narrow. Any future integration should retain the current data-only AssetBundle validator, authenticate script packages separately, require exact-content/capability consent, apply successful bounded commands on Unity's main thread and revoke the whole script scope before asset teardown. The current prototype only stages in-memory objects and has no production execution or consent path.

## Verification

- Luau native Release build: passed using VS2022 x64 and the official pinned commit `9d7748a4f324213a18cb8283fa2a77c330225fa0`.
- Prototype Release build: passed, 0 warnings / 0 errors after an online exact-package restore/audit request. The earlier restricted restore could not reach NuGet's audit feed; that was resolved before the final build.
- Full comparison: 64/64 expected outcomes; 6/6 host checks; 64/64 exact sentinel checks; zero script loopback connections.
- Existing `Anomaly.AssetValidation.Tests`: **61/61 passed**. Initial restricted execution could not connect VSTest to its testhost; rerunning with normal local IPC passed.
- SHA-256 comparison: **50 existing validator/client asset/content/shared asset-contract files unchanged** against the pre-prototype baseline. Independent source review also covered the unchanged file-transfer implementation. Preexisting dirty worktree changes were preserved.
- No existing runtime project, solution membership, compile includes, wire protocol, AssetBundle allowlist, release packaging or deployment target was changed. No Unity, MelonLoader, game/server or IL2CPP runtime validation was performed.

Commands used from the repository root:

```powershell
pwsh -File tools/ScriptSandboxPrototype/Build-Luau.ps1
dotnet restore tools/ScriptSandboxPrototype/ScriptSandboxPrototype.csproj -p:DeployLocal=false
dotnet build tools/ScriptSandboxPrototype/ScriptSandboxPrototype.csproj -c Release --no-restore -p:DeployLocal=false -p:UseSharedCompilation=false
dotnet tools/ScriptSandboxPrototype/bin/Release/net6.0/Anomaly.ScriptSandboxPrototype.dll
dotnet test Anomaly.AssetValidation.Tests/Anomaly.AssetValidation.Tests.csproj -c Release --no-build --no-restore -p:DeployLocal=false
```

Native bridge SHA-256 for the observed run: `EF09ED090A0A6AC013D3C728AEBE8ED9F8729BEA06876630F2509D193CAB56FB`. MoonSharp package content identity is pinned in `packages.lock.json`. Local outputs and third-party checkouts remain ignored; neither engine was added to production packages.
