# Client scripting

`Anomaly.Client.Scripting` is the client-owned MoonSharp host. It references
`Anomaly.Client.Api` and the source-pinned, privately named
[`Anomaly.MoonSharp.Interpreter`](../third_party/MoonSharp/README.md) fork.
The API project has no interpreter dependency. Runtime entry points are internal
to the client and its tests; this is not yet a public mod scripting service.

**Server-supplied execution is disabled.** `ScriptingPolicy.MemoryHardeningQualified`
and `RemoteExecutionEnabled` are unconditional `false` getters. `CreateRemoteSession`
rejects before validating source, allocating a VM, or loading a package. No setting,
server trust, signature, consent record, event, AssetBundle or file-transfer message
can enable execution. Client startup installs an inert host with no script sessions.
AssetBundle validation and its transfer/consent path are unchanged.

## Opt-in object bindings

Annotations belong to `Anomaly.Client.Api.Scripting` so API types can declare their
reviewed script surface without referencing MoonSharp:

```csharp
[ScriptType]
public sealed class Example
{
    [ScriptVisible]
    public string Name { get; set; } // Lua gets a read-only property.

    [ScriptVisible]
    public void SetLabel(string label) { /* bounded, capability-scoped work */ }

    [ScriptIgnore]
    public object InternalHandle { get; }
}
```

`DiscoverApi` automatically collects `[ScriptType]` declarations from the known
`Anomaly.Client.Api` assembly. Bootstrap does not maintain a list of individual
types. It never scans arbitrary loaded assemblies or loads assemblies supplied by
a server. Trusted development code can explicitly register fixture types separately.
Discovery validates the complete batch before committing it and does not construct
host objects, run their static constructors, or invoke property getters.

The target-aware reader uses `Type.Name` or the declared member's exact C# name.
Thus `Player.PlayerId`, `Player.Nickname` and `ClientReadyEventArgs.SceneName` retain
their C# spelling. An optional annotation argument supplies an explicit alias, for
example `[ScriptVisible("LegacyName")]`. Explicit invalid/empty names, Lua keywords,
ambiguous overloads and duplicate aliases are rejected. Renaming C# declarations
changes the script contract and requires a reviewed baseline update.

Only annotated, declared instance properties/methods are bound. `AllowWrite = true`
opts a selected public setter in; `ScriptIgnore` takes precedence. Constructors,
static members, inherited members, fields, indexers, raw events, generic methods,
`GetType`, reflection objects, delegates and arbitrary collections are unavailable.
Arguments/results must be finite scalars, bounded UTF-8 strings or explicitly approved
reference types. Exact runtime types are checked, including returned values; unapproved
subclasses are rejected. A returned object cannot broaden the approved type graph.

Descriptors belong to one script and registry. They do not use MoonSharp's global
type registration or automatic CLR binding. Property access and callbacks check the
current execution budget before and after C# work. A C# method must itself have bounded
work and inputs: the interpreter cannot interrupt a blocking managed/native method
while that method is running.

The initial surface in [`ScriptAPI.Shipped.txt`](ScriptAPI.Shipped.txt) exposes only
three read-only Player properties and the selected read-only connection/ready data.
Type discovery does not create a global instance, constructor, or static API. Host
instance globals are separate from type names; an object named `player` is accessed
as `player.PlayerId`. Tests pin this surface separately from the managed API baseline.

The immutable catalog is the shared source for binding names and permissions, the
reviewed surface baseline, [`ScriptAPI.json`](ScriptAPI.json), and the
[`LuaLS definitions`](Anomaly.Client.Api.d.lua). It contains only the approved profile,
with CLR identities, parameter/return types, write permissions, event operations,
and XML documentation identifiers. Optional adjacent API XML summaries enrich exports.
No exporter independently walks every public API member or constructs game objects.

## Calling scripts and delivering events

Trusted client development code can construct the internal host:

```csharp
using var runtime = new ScriptRuntime();
runtime.DiscoverApi(); // Example is discovered if declared in Anomaly.Client.Api.
using var session = runtime.CreateDevelopmentSession();
session.SetGlobal("example", example);
var loaded = session.Load("function Describe() return example.Name end");
if (loaded.Succeeded)
{
    var result = session.Invoke("Describe"); // Bounded primitive result, not DynValue.
}
```

Lua calls methods as `example:SetLabel("hello")`. C# calls named Lua functions through
`Invoke`, which creates a fresh cumulative budget for that entry point, including
argument conversion. Source parsing/loading uses the same budgeted path. Failures
retire the VM rather than resuming a partially unwound interpreter.

The event registry follows SCriPt's category discovery and lazy subscription model.
Public static events marked `[ScriptEvent]` in the API's exact event-handler namespace
are collected automatically. Their names retain C# casing; `ClientEvents` supplies
the `Client` category. An optional event alias is supported and `[ScriptIgnore]` wins.
Only void zero/one-argument delegates and validated read-only data graphs are admitted.
The initial profile includes `Events.Client.Connected` and `Events.Client.Ready`.

Lua subscribes with `Add(callback)` and removes by closure or opaque returned token.
Dot and colon calls are supported. The first subscription attaches one CLR delegate,
shared across sessions; the last removal detaches it. Removing a closure removes all
its registrations for that event/session; removing a token removes one. A removed
registration's queued deliveries remain invalid even if the same closure is added again.
The CLR event handler captures bounded read-only data and enqueues callbacks. It never
calls Lua. `Tick` dispatches queued handlers through the
same budgeted invocation path, with a shared two-millisecond wall budget per tick,
at most four deliveries, and round-robin session selection. These are checkpoint
limits, not a hard real-time preemption guarantee. Lua can define:

```lua
local function OnReady(args)
    -- args.SceneName is a frozen read-only value.
end

local token = Events.Client.Ready.Add(OnReady)
-- Events.Client.Ready.Remove(OnReady) or Events.Client.Ready.Remove(token)
```

Event data is copied before the publisher can mutate or reuse it. Snapshots allow
only approved read-only properties, exact runtime types, depth 8, 64 nodes, 4 KiB per
string and 16 KiB of accounted payload. They expose no methods, writes, reflection or
conversion back to live CLR arguments. These are transport bounds, not a VM heap quota.
Foreign-thread publication or invalid snapshots request owner-thread retirement
without running Lua or reading game properties on the publishing thread. Queued
events are notifications; they cannot synchronously cancel or modify a native hook.

There are at most eight development sessions, 32 subscriptions per session and 64
queued deliveries per session. Queue overflow retires the session. Synchronous script
reentry is rejected; scripts receive no raw CLR event/delegate objects.
Disconnect, scene reset and shutdown revoke sessions before stale game references
can be reused. Tracked subscriptions/resources are disposed in reverse order without
calling script cleanup hooks; one ordinary resource or diagnostic observer failure cannot
prevent the remaining cleanup. Runtime/session access is owner-thread-only.

## CPU limits and restricted libraries

The fork shares terminal fuel, monotonic deadline and depth checks across main execution,
coroutine resumes, protected calls, library callbacks and C# reentry. It also charges
parser/compiler work, table operations, string processing and pattern backtracking.
`pcall`, `xpcall` and `table.sort` cannot turn budget exhaustion into a recoverable Lua
error. A retired script cannot regain access by resuming a coroutine or calling again.
`AutoYieldCounter` supplies scheduling slices; it is not the security limit.

Sessions allow a selected subset of Basic, Math, Table, String and protected-call
functions. Filesystem, OS, networking, debug, module loading, bytecode dumping/loading,
coroutine creation and automatic CLR interop are absent. Source and returned strings
have byte limits. Only host-approved bounded object operations can add capabilities.

## Memory qualification still required

There is no per-script managed heap quota. Lua tables, strings, closures, coroutine
stacks and compiler objects still allocate in the game's process. CPU fuel and source
limits reduce some allocations but do not establish a memory boundary. An out-of-memory
exception, task timeout or process watchdog is not successful VM containment.

Before any future remote enablement, implement and review allocation accounting and
pre-allocation limits across those paths, including transient builtin/host buffers,
and make memory termination sticky across protected calls and callbacks. Regression
probes must reject growing tables, repeated strings, closure/coroutine growth and
allocation-error loops inside the VM, without process termination, and verify cleanup
and repeated session churn. Capability-scoped object ownership, a reviewed script
transport/manifest/consent contract and live Unity validation also remain required.
Enabling remote execution requires a deliberate code change after qualification;
this implementation adds no dormant switch for servers to activate.

## Verification

```powershell
dotnet build Anomaly.Client/Anomaly.Client.csproj -c Debug -p:DeployLocal=false
dotnet test Anomaly.Client.Scripting.Tests/Anomaly.Client.Scripting.Tests.csproj -c Debug -p:DeployLocal=false
dotnet test Anomaly.AssetValidation.Tests/Anomaly.AssetValidation.Tests.csproj -c Debug -p:DeployLocal=false
dotnet run --project tools/ScriptApiCatalog -- Anomaly.Client.Scripting --verify
```

After reviewing an intentional script API change, regenerate the three catalog files
with `dotnet run --project tools/ScriptApiCatalog -- Anomaly.Client.Scripting`.
The tool discovers metadata and exports definitions without creating a script VM.

CPU abuse fixtures run in separate .NET 6 workers. Their Windows job/GC ceilings and
independent watchdog protect the test runner; a watchdog kill fails the test. These
safeguards are not interpreter memory limits. Builds/tests do not prove live game-frame
performance or IL2CPP event behavior. Normal builds do not deploy or start the game.
