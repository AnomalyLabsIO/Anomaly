# Anomaly MoonSharp fork

This ignored local directory contains MoonSharp's interpreter from the official repository at the full commit
[`fbd03678d5c0f731d731d099211c7f4a3a1ae8b7`](https://github.com/moonsharp-devs/moonsharp/tree/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7).
The original source reports `3.0.0.0`; the corresponding source build was identified as
`3.0.0-beta.1.16+fbd03678d5c0f731d731d099211c7f4a3a1ae8b7`.

Only the 247 interpreter C# source files and upstream license were copied. No Git metadata,
signing keys, debugger sources, binaries, or intermediate build outputs were imported.
`upstream-source-manifest.json` records each original source file's SHA-256 before Anomaly
modifications. Paths are relative to upstream `src/MoonSharp.Interpreter` and local `src`.
`anomaly-hardening.patch` records the source changes and added engine files against that baseline.
The tracked restore inputs and patch are in `tools/MoonSharpHardening/` at the repository root;
upstream source files remain untracked.

The project builds `Anomaly.MoonSharp.Interpreter.dll` for .NET 6. It retains MoonSharp's namespaces
but uses a distinct assembly name and does not strong-name sign. This prevents accidental assembly
identity reuse with an unrelated MoonSharp distribution. The namespace is not a security boundary.
The fork has no package or game/Unity dependencies. Source license is **BSD-3-Clause**, retained
in [LICENSE](LICENSE), with KopiLua/Lua MIT notices retained in their source files. The project
overrides the repository's default package license metadata accordingly.
The current Anomaly hardening revision is `3.0.0-anomaly.6`.

## Host execution budget

```csharp
script.ExecutionBudget = new ExecutionBudget(
    instructionLimit: 200_000,
    timeout: TimeSpan.FromMilliseconds(100),
    maxCallDepth: 128);
```

`ExecutionBudget` is an optional host-only object. An unset budget preserves the unbounded
upstream execution model. Never expose `Script`, the budget, or budget setters to untrusted Lua.
Assign the budget before loading untrusted source so parsing and compilation are included.

`Charge(work)` consumes cumulative work and checks the deadline/cancellation state; `Check()` checks
deadline/cancellation without charging additional work. `Cancel()` requests terminal cancellation
at the next checkpoint. The fuel measure includes VM instructions, source/compiler work, builtin
loops, string processing, and callback entry; it is not a Lua instruction count or real-time guarantee.
One budget is shared across nested calls, coroutine creation/recycling, and all resume slices.
It is never reset automatically on yields, Lua protected calls, or callback reentry.

Revision `.6` adds trusted-host-only `PauseForHost()`, returning an allocation-free scope.
A synchronous worker RPC wait pauses only elapsed engine deadline time; disposing the scope
restores deadline accounting. Fuel consumed, call depth, cancellation and terminal
state are never reset. It is not exposed to Lua and cannot turn arbitrary managed
or native work into exempt execution. The parent's independent five-second command
watchdog continues while the engine deadline is paused.

Thread-local execution scopes restore the previous budget on return, yield and
failure. Numeric string coercion and shared table/index operations use the active
invocation's budget; a retained prime table never captures an old work budget.
Its memory ownership remains attached to its original allocation allowance.

Exhaustion throws `ScriptExecutionAbortedException`, outside Lua's catchable `ScriptRuntimeException`
hierarchy. `Reason` / `TerminationReason` is `CpuLimit`, `Deadline`, `DepthLimit`, or `Cancelled`.
Cancellation is sticky: later calls with that budget fail before script execution or host callbacks.
The host must retire the script, revoke its capabilities and queued work, and discard staged changes.
Do not replace a terminated budget to revive a damaged interpreter or run an untrusted cleanup hook.

`Coroutine.AutoYieldCounter` remains a separate cooperative scheduling facility. Hard budget checks
also run in the main processor and non-yieldable callback reentry. A hard stop aborts; it does not
pretend that an arbitrary managed call stack can be safely suspended as a coroutine.

## Focused changes

- Shared work/deadline/depth checks at VM entry, instruction dispatch, native callback boundaries,
  nested calls and coroutine resume boundaries; terminal state bypasses Lua error decorators.
- Preserve terminal aborts through `table.sort`'s .NET comparer exception wrapping and through
  protected-call paths. Check again after a trusted callback returns, even if it swallowed an abort.
- Bound call/reentry depth and preflight bounded VM stack capacity. Parser expression/statement depth,
  compilation recursion and lexer/compiler work participate in the same budget.
- Charge table traversal/copying/sorting, including ownerless prime-table scans,
  string-key dictionary hashing/equality, numeric string coercion, string comparison/
  concatenation and native string work. Charges precede parsing/hashing; repetition
  charges calculated output work and memory reservations before allocating.
- Check pattern scanning/backtracking, callback reentry, formatting output and pattern recursion.
  Restore pattern recursion depth on return rather than consuming it permanently per attempt.
- Resolve .NET 6's `ReferenceEqualityComparer` name conflict explicitly.

## Scope and validation

This is CPU and experimental allocation hardening for Anomaly's restricted scripting host, not a claim that every upstream module
and reflection API is safe for hostile scripts. The host still controls its library allowlist,
primitive/annotated object surface, loaders, source limits, events, timers and lifecycle cleanup.
Raw CLR events must not bypass the host scheduler. Exposed C# operations must have bounded work and
input sizes; an engine checkpoint cannot interrupt an arbitrary blocking or infinite managed/native
method while it is running. Large builtin operations can be rejected conservatively by work charges.

**There is no qualified per-script CLR heap quota.** The host can install reclaimable accounting:

```csharp
var runtimeMemory = new MemoryBudget(64L * 1024 * 1024, reclaimable: true);
var sessionMemory = new MemoryBudget(16L * 1024 * 1024, runtimeMemory, reclaimable: true);
var script = new Script(CoreModules.None, sessionMemory);
using (sessionMemory.EnterScope())
{
    // Host-created arguments and library registration participate in the same allowance.
}
```

`Allocate(bytes, factory)` and `AllocateString(chars, factory)` reserve before invoking the factory
and bind the reservation to its result. For other backing owners, use a scoped ticket:

```csharp
using var reservation = MemoryBudget.Reserve(capacityEstimate);
var buffer = CreateBoundedBuffer();
reservation.Bind(buffer); // Dispose cancels only if no owner was committed.
```

`ReserveString(chars)` checks UTF-16 length arithmetic. `Charge(bytes)` has unknown lifetime and
remains permanent. `MemoryLimit` is sticky and outside Lua
protected calls. Entry points, coroutine resumes and callbacks install/restore the owner scope,
and post-callback checks catch a swallowed terminal abort. Child budgets also charge an optional
parent. Parent/child accounting modes must match. Admission is atomic across the parent chain;
aggregate denial terminates the requesting reclaimable child without poisoning healthy siblings.

Reservations cover tables (including ownerless prime tables), dictionary growth, VM stacks that
grow from 64 slots, frames/arguments/tuples, values/closures, strings, pattern/format buffers and
conservative source/compiler overhead. The shared registry reserves 256 bytes per tracked owner
and caps tracking at 131,072 records. Long weak references credit committed storage only after
collection, including finalization/resurrection. Pending tickets cancel on disposal; bound tickets
cannot refund live storage. Dictionaries retain capacity charges after Clear/Remove, and aliases,
nested event snapshots and host-escaped strings retain appropriate backing/retention owners.
Session disposal or loss does not refund storage retained elsewhere.

`Sweep(maxRecords)` only observes collection and never forces global GC or invokes user code.
The host sweeps bounded batches at entry/tick and can scan the bounded registry under pressure.
`ChargedBytes` reports outstanding estimates, `TotalAllocatedBytes` cumulative reservations,
`PeakBytes` peak outstanding estimates, and `UnboundBytes` unknown/pending charges. These do not
measure live CLR heap or RSS. Delayed GC or conservative estimates may reject a valid script.
The engine defaults to legacy cumulative mode unless explicitly opted in, preserving existing
direct callers. No collection credits are issued in that mode.

Source/parser scratch is conservatively estimated, and CLR framework/diagnostic overhead and
arbitrary native or trusted C# callback allocations are not completely instrumented. Unexposed
modules, binary stream/dump paths and arbitrary interop are outside the host profile and have not
been qualified. Bounded host code must reserve before allocating its own large buffers. An optional
null allowance retains upstream's unbounded memory model. Never replace a session's allowance,
clear terminal state, manually credit live storage, or expose budget objects to scripts.

Remote script execution requires a separate Windows AppContainer/job worker with
actual successful isolation probes and exact content/execution approval; unavailable
isolation fails closed. Local CLR memory accounting remains unqualified. Verified
local hosted sources retain their separate entry point; raw remote source rejects.
Engine regression evidence alone does not establish live game/frame acceptance.

Build the isolated fork:

```powershell
dotnet build third_party/MoonSharp/Anomaly.MoonSharp.Interpreter.csproj -c Release -p:DeployLocal=false
```

Run CPU regressions through the process-isolated parent, which treats its watchdog kills as failures:

```powershell
dotnet test Anomaly.Client.Scripting.Tests/Anomaly.Client.Scripting.Tests.csproj -c Debug -p:DeployLocal=false --filter FullyQualifiedName~CpuExecutionBudgetTests
```

The fixed worker fixtures live in `tools/ScriptingCpuProbe`. They exercise main and coroutine loops,
protected calls/error handlers, builtin callbacks/pattern work, C# reentry, cumulative fuel, depth,
cancellation, deadlines after finite C# work, and rejection of later calls after termination.
Fresh-entry fixtures also cover retained numeric strings and long table keys,
prime-table iteration/length/dead-key scans, and bounded useful work after the setup
budget ends. Direct `tonumber` string arguments already participate in callback-entry charges.
Independent process/GC ceilings used by the test parent are experiment safeguards, not VM memory limits.
Memory regressions likewise require a terminal engine `MemoryLimit`; watchdog/GC/process failures
always fail. They exercise large and discarded strings, tables, closures, coroutine churn, source
compilation, transient buffers, protected calls, callbacks, cumulative legacy behavior and cleanup.
Reclaimable workers exercise 10,000 bounded calls in both engine and host paths, retained values,
capacity, shared ownership, escaped snapshots, abandoned children, pinning and resurrection.
Explicit full GC is confined to the isolated workers; production GC timing remains unqualified.

The CLR/host audit additionally preflights compiler collection growth, ordinary Lua
exceptions/diagnostic formatting, primitive/tuple output and random reseeding. See
[ALLOCATION-AUDIT.md](../../Anomaly.Client.Scripting/ALLOCATION-AUDIT.md) for the
reviewed paths and remaining CLR/native/approved-method trust boundaries.
