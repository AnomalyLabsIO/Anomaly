# Fixed CPU and allocation regression worker

This .NET 6 tool references Anomaly's privately named MoonSharp fork and the internal
client scripting host. Its command argument selects a fixed fixture; it accepts no
script file, source text, URL, or assembly path. The fixed host-session fixture uses
the production session invocation/maintenance code with no game instance or native
Unity operation. Host/API dependencies, including MelonLoader, are copied for that
standalone managed test; no client mod or server is launched.

The CPU and memory execution tests launch one worker for each fixed fixture. On
Windows it attaches a 256 MiB process Job Object before sending the worker's stdin
start handshake. A 128 MiB CLR heap setting is an additional experiment safeguard.
The parent kills a worker that exceeds five seconds and fails that test: a worker
kill is never counted as a successful interpreter limit.

Run through the parent rather than executing adversarial fixtures unguarded:

```powershell
dotnet test Anomaly.Client.Scripting.Tests/Anomaly.Client.Scripting.Tests.csproj -c Debug -p:DeployLocal=false --filter FullyQualifiedName~CpuExecutionBudgetTests
```

Coverage includes ordinary main/coroutine loops, protected calls, an error-handler
loop, standard-library callbacks, C# callback reentry, pattern-matching work inside
a builtin, cumulative fuel across resumes and sibling coroutines, bounded useful
work, runtime/source recursion depth, cyclic `__call` metadata, nested CLR
protected-call chains, cancellation,
expired deadlines, finite CLR work returning
after its deadline, and rejection of a terminated script's later calls. A trusted
wrapper catching the abort must not convert terminal state to successful execution.

The worker reports the actual `ScriptExecutionAbortedException.Reason`. Fuel tests
use a longer independent deadline so a deadline does not masquerade as working
fuel accounting. The pattern fixture uses a small host-created input to isolate
builtin CPU work from allocation and parsing. These CPU fixtures avoid deliberate memory growth.

The legacy `memory-*` fixtures use the host-only cumulative allocation allowance before VM
initialization. After finite setup, most explicitly reserve the unused allowance
to leave a small known remainder, isolating the allocation path being checked.
They cover large repetition, repeated formatting, protected-call aborts, arrays,
tables including `${...}` prime tables, concatenation, discarded strings,
closures, source compilation, `table.concat`/`unpack`/`sort`, comparator failures,
coroutines, callback scope and post-checks, and accounting across separate calls.
The direct Lua coroutine fixture enables that module only inside this worker;
production sessions do not install it. Successful abuse containment requires the
actual terminal `MemoryLimit` exception; process OOM, watchdog kills, CPU limits
and wrong exceptions fail the regression.

Two precompiled-call fixtures also record `GC.GetAllocatedBytesForCurrentThread`
around an 8 MiB repetition request and an 8-million-value unpack request. They
must terminate with `MemoryLimit` while allocating less than 4 MiB on that thread;
a late rejection after constructing the requested 16 MiB string buffer or 64 MiB
reference array fails this check. This measures allocation during that call, not
live heap size or peak process memory.

The `reclaimable-*` fixtures opt into outstanding-allocation accounting. They test
10,000 bounded direct-engine calls and 10,000 actual host-session invocations, each
producing a temporary 1,024-character string. Their total accounted allocation must
exceed the 16 MiB allowance while outstanding charges stay within it. The host path
creates and retires its normal per-invocation coroutine and must leave no unbound
allocation debt. Explicit full GC runs only in this isolated worker between batches;
the host observes collection through its normal `Tick` sweep and never forces GC.
This experiment demonstrates reuse after collection, not guaranteed production GC
timing or uninterrupted game-frame performance.

Further fixtures cover retained strings/tables/closures/prime tables, actual
`DynValue.Assign`/overwrite, shared string clones, retained dictionary capacity,
bound/unbound ticket disposal, throwing factories, merged reservations, abandoned
child budgets, pinned objects and finalizer resurrection. Live storage remains
charged until it is genuinely unreachable, including after child-scope loss. An
aggregate denial must terminate only the allocating child and leave recovered pool
capacity available to a fresh child. Sweeping is bounded observation, not a global
collection request. Unknown-lifetime accounting remains conservative; a script may
still be denied before a natural CLR collection.

```powershell
dotnet test Anomaly.Client.Scripting.Tests/Anomaly.Client.Scripting.Tests.csproj -c Debug -p:DeployLocal=false --filter FullyQualifiedName~Memory
dotnet test Anomaly.Client.Scripting.Tests/Anomaly.Client.Scripting.Tests.csproj -c Debug -p:DeployLocal=false --filter FullyQualifiedName~Reclaimable
```

These are engine resource regression checks in standalone .NET 6 processes. The
legacy cumulative allowance never refunds values. The opt-in reclaimable allowance
credits tracked backing owners only after collection; neither mode is a measurement
or ceiling for the live CLR heap. The external
process/GC ceilings do not establish a per-script heap limit. These tests do not
qualify hostile remote execution, game-frame performance, Unity calls or headset behavior.
Arbitrary trusted C# methods remain responsible for bounding
their own work; the finite-delay fixture checks the boundary after one returns.

The `clralloc-*` fixtures check diagnostic/exception, compiler-container, reseeding
and tostring preflight, no-copy whitespace scanning and reclaimed ordinary errors.
The `hostalloc-*` fixtures check retained argument/diagnostic storage, bounded binding
churn and event getter reentry. `hostalloc-uncooperative-clr-body` deliberately records
a finite allocation inside an approved C# method that the VM cannot intercept; it is
a limitation regression and does not count as memory-containment success. See
`Anomaly.Client.Scripting/ALLOCATION-AUDIT.md` for the evidence boundary.
