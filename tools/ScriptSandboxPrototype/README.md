# Client scripting sandbox prototype

This standalone Windows x64 / .NET 6 experiment compares MoonSharp and Luau using the same small object capability API. It does not reference Unity, MelonLoader, or any Anomaly runtime project. The objects are in-memory stand-ins, not rendered objects. Nothing is deployed, no game is launched, and no production AssetBundle/transfer/consent code is changed.

## Run

Prerequisites: .NET 6 runtime and a .NET SDK, PowerShell, Git, and VS2022 C++ x64 build tools/CMake. `Build-Luau.ps1` defaults to the CMake bundled with VS2022 Community; override `-CMake` if installed elsewhere. The first build downloads official source at the exact pinned Luau commit and restores the pinned MoonSharp package. Later native builds require that checkout to remain clean and match the pin.

From the repository root:

```powershell
pwsh -File tools/ScriptSandboxPrototype/Run-Comparison.ps1
```

For an already-built native bridge:

```powershell
pwsh -File tools/ScriptSandboxPrototype/Run-Comparison.ps1 -SkipNativeBuild
```

One runtime/probe can be selected with `-Runtime moonsharp` or `-Case reentrant_string_callback_loop`. The executable also accepts `--runtime`, `--case`, and `--output` directly. Reports go to `artifacts/script-sandbox/comparison/comparison.{json,md}` unless overridden. Output must remain inside this checkout. A restrictive execution sandbox may need a normal local execution permission for the loopback listener and testhost IPC. Do not interpret a blocked listener as successful script network isolation.

To test a source-built MoonSharp DLL instead of the pinned 2.0.0 NuGet baseline:

```powershell
pwsh -File tools/ScriptSandboxPrototype/Run-Comparison.ps1 -SkipNativeBuild -Runtime moonsharp -MoonSharpAssemblyPath 'E:\Projects\SCPSL\SCriPt\SCriPt\libs\MoonSharp.Interpreter.dll'
```

This selects `LocalMoonSharp/ScriptSandboxPrototype.LocalMoonSharp.csproj`, linking the same harness sources with a direct reference and separate build/output directories. The supplied DLL is copied, never modified. Reports default to `artifacts/script-sandbox/moonsharp-local`; override `-OutputDirectory` to retain multiple runs. Every newly generated report records the actually loaded MoonSharp assembly/version/framework/MVID/SHA-256 and the harness binary hash. See [MOONSHARP3.md](MOONSHARP3.md) for the completed current-source comparison and provenance.

The tool checks expectations, including known containment failures; a `PASS` for an expected `ProcessTimeout` records a reproducible limitation, not a secure VM outcome. See [COMPARISON.md](COMPARISON.md) for the observed results and recommendation.

## Script API

```lua
local cube = objects.spawn('demo/cube')
objects.set_position(cube, 1, 2, 3)
objects.set_label(cube, 'Client object')
objects.destroy(cube)
```

The only approved asset keys are `demo/cube` and `demo/panel`. Spawn returns a random owner-scoped string handle. Operations validate the current scope and handle, finite coordinates within +/-1000, a 128-byte UTF-8 label budget, 16 live objects, and 128 host-call attempts. Both adapters additionally count malformed argument attempts before type validation and reject callback strings above 4096 UTF-8 bytes. Failed validation does not partially mutate an object. Revocation clears the scope and rejects stale operations. No raw CLR/Unity objects, reflection, delegates, arbitrary properties, paths, URLs, or network transports are exposed.

The in-memory batch is discarded when execution does not complete. A future Unity integration must apply validated successful batches on the main thread, bind handles to connection generations, budget downstream Unity costs, and dispose all script-owned resources before assets unload. Those integration steps are not implemented here.

## Containment design

- **MoonSharp 2.0.0 / current source-built 3.0:** baseline uses an exact NuGet version plus lockfile; the alternate project takes an explicit binary reference. The shared adapter starts with `CoreModules.None`, registers selected libraries, then applies a positive globals list. Only primitive callbacks are exposed. Automatic CLR registration is disabled, file/module loading rejects, and the platform accessor is restricted. Source initialization executes through a host-owned coroutine with 10,000-instruction slices, 20 resumes and a 100 ms elapsed check. No stock coroutine module is exposed. Both tested versions provide no guest heap quota here and cannot promptly preempt every tested non-yieldable library callback.
- **Luau 0.743:** official source is pinned by full commit in `native/luau-pin.json`. CMake builds a small native C ABI bridge and the VM/compiler, without a third-party .NET binding. Positive globals and frozen library/API tables restrict capabilities. A custom allocator limits requested live VM allocation bytes to 4 MiB, including initialization/load/execution. Native charged-step slices and a deadline terminate runaway scripts, with sticky CPU/memory states surviving `pcall`. The debug single-step hook skips some upstream instruction types and deliberately slows execution: its steps are not an exact universal instruction count or a production performance configuration. Only locally compiled source reaches the VM. Errors are raised in native code after managed callbacks return, so Lua unwinding does not cross reverse P/Invoke frames.
- **Independent experiment safeguards:** every probe runs in a new process, assigned to a Windows Job Object with a 256 MiB committed-memory cap before execution. The parent kills after 2 seconds or a sampled 192 MiB private-memory threshold; a .NET 128 MiB GC heap cap provides another host safeguard. These are process safeguards, not guest VM quotas. There is no AppContainer, filesystem/network OS sandbox, or claim that an unrestricted helper process alone protects user files.

The compiler/parser and managed host allocations are outside Luau's VM allocator budget. Source is capped at 64 KiB before untrusted compilation; the process safeguards cover remaining compiler abuse for this experiment. Real native/compiler allocation failure is classified separately from configured VM quota denial. No test can prove absence of interpreter/native binding vulnerabilities.

## What is checked

Each worker first reads the task-owned sentinel and makes a counted loopback control connection, proving its OS file/network access is available. Script probes then attempt actual reads, writes, deletion, process launches, CLR/module loading and HTTP calls. Only the task-owned sentinel and loopback endpoint are targets. The parent requires exact sentinel preservation and zero script connections, including after crashes or watchdog kills. The listener tolerates peer disconnects and must remain active. Read rejection is also asserted inside the script; unchanged file bytes alone do not prove reads were blocked.

The suite includes real foreign-scope/revoked-handle host checks, approved behavior, invalid scalar types, NaN/infinity/bounds, UTF-8 limits, object/attempt quotas, rollback after script error, infinite and protected-call loops, a reentrant string callback, allocation above the VM quota, repeated swallowed allocation failures, incremental memory growth, binary/dump input rejection, source-size rejection, and parser failures.

Timing includes per-VM initialization and compilation after trusted assembly/JIT warm-up. MoonSharp's elapsed check includes initialization/parsing; Luau's native deadline starts after compilation, which remains covered by the process watchdog. Managed allocation deltas and sampled process peaks differ from Luau allocator accounting; do not compare them as equivalent heap measurements. Short-lived process peaks can be missed. Windows process CPU time is coarsely quantized; zero does not mean no work. This is not a game FPS/latency benchmark.

## Production boundary

The prototype is intentionally absent from `Anomaly.slnx`, runtime project references, package layout and deployment targets. Asset-only approval does not authorize these scripts. A future production implementation requires separately authenticated script content, exact-content/capability consent, lifecycle integration, API versioning and adversarial review before any execution path is enabled.

Primary references: [MoonSharp sandboxing](https://www.moonsharp.org/sandbox.html), [coroutine caveats](https://www.moonsharp.org/coroutines.html), [loader boundary](https://www.moonsharp.org/scriptloaders.html), [Luau embedding/sandbox model](https://luau.org/sandbox/), [official Luau source](https://github.com/luau-lang/luau). See [THIRD-PARTY.md](THIRD-PARTY.md) for exact dependency provenance.
