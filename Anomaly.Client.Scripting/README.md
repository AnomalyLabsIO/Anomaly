# Client scripting

`Anomaly.Client.Scripting` is the client-owned MoonSharp host. It references
`Anomaly.Client.Api` and the source-pinned, privately named
[`Anomaly.MoonSharp.Interpreter`](../tools/MoonSharpHardening/Fork-README.md) fork.
The upstream tree remains ignored; pinned restore inputs and the hardening patch
are kept in [`tools/MoonSharpHardening`](../tools/MoonSharpHardening/README.md).
The API project has no interpreter dependency. Runtime entry points are internal
to the client and its tests; this is not yet a public mod scripting service.

**Remote execution requires qualified process isolation and explicit approval.**
`MemoryHardeningQualified` remains false: local CLR accounting is not a heap ceiling.
`RemoteIsolationQualified` is published only after actual Windows AppContainer/job
qualification; `RemoteExecutionEnabled` follows that result. Unsupported platforms,
Wine, missing runtime/payloads or failed probes fail closed, without in-process fallback.
Verified local hosted sources and development tests retain their separate entry point.
Raw remote source strings reject before VM creation. Startup discovers
metadata but runs no scripts. The authenticated content catalog binds generated
script-pack indexes and exact source files; execution approval applies only to the
current connection and offer. See [`SCRIPT-PACKS.md`](SCRIPT-PACKS.md) for owner
directories, embedded plugin resources, generated metadata, and initialization order.

Schema-1 script-pack indexes also declare named, typed messages. The host installs
pack-bound `Messages.On`, `Messages.Off`, and `Messages.Send` before initialization,
using the existing authenticated transport and server plugin
`ClientMessaging`/`ClientEvents` APIs. Callbacks share Events' subscription, queue,
and execution limits; outbound transport requires current content admission and
player readiness. See [MESSAGING-DESIGN.md](MESSAGING-DESIGN.md) for schemas and Lua.

## First useful local script

For local owner execution, use the verified nondedicated in-process host path;
remote packs instead require qualified LPAC/job isolation and separate approval.
Copy the complete
[`samples/HelloAnomalyServer/ContentPacks/example/panel`](../samples/HelloAnomalyServer/ContentPacks/example/panel)
directory to `<UserData>/Anomaly/ContentPacks/example/panel/`, retaining `pack.json`,
`scripts/main.lua` and `scripts/messages.json`, then start a local host.
The descriptor selects one entrypoint; unlisted Lua never executes.
The sample logs initialization and shows local state on future Ready/player-added notifications.
This local-owner installation does not run in menus or while joining a remote server;
the server may separately offer the embedded example through the approved remote path.
Each logical script pack has one VM; packs do not share globals or import modules.
Changes take effect on the next connection, not by hot reload.

Every runtime-created session receives a host-owned `Output` capability:

```lua
Output:Log("Initialized") -- One bounded line with a host-controlled source prefix.
Events.Client.Ready.Add(function()
    local shown = Output:ShowText("Hello from Lua", 5)
    -- false means invalid/rate-limited input, exhausted display slots, or UI not ready.
end)
-- Output:ClearText() clears only this session's text.
```

Text is literal, not rich text; no UI/native object or input capture is exposed.
Both calls return `false` for ordinary rejection. `ClearText` returns no value.
Logs and displays accept at most 1024 UTF-8 bytes; displays allow eight lines and
finite durations from 0.1 through 10 seconds. Logs reject newlines. Control/format
characters and malformed UTF-16 are rejected. Independent fixed one-second windows
allow four logs/two display updates per session and sixteen logs/eight display
updates across a runtime. At most eight owners display text simultaneously.
Expiry, clear, session retirement and runtime shutdown release only the owner's
label; no Lua unload callback is needed. A display reservation is a conservative
workspace allowance, not measured native memory containment.

Use [`Anomaly.Client.Api.d.lua`](Anomaly.Client.Api.d.lua) as a LuaLS
`workspace.library` entry, not as an executable script. The SDK includes this
guide, pack/message guides, exports and the sample's actual embedded resources.


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

`DiscoverApi` collects only `[ScriptType]` declarations from the known
`Anomaly.Client.Api` assembly. Each exposed property or method needs `[ScriptVisible]`
on its declaration. There is no separate type/property allowlist, automatic subclass
registration, or public-member registration fallback. It never scans arbitrary loaded assemblies or loads assemblies supplied by
a server. Trusted development code can explicitly register fixture types separately.
Discovery validates the complete batch before committing it and does not construct
host objects, run their static constructors, or invoke property getters.

The target-aware reader uses `Type.Name` or the declared member's exact C# name.
Thus `Player.PlayerId`, `Player.Nickname` and `ClientReadyEventArgs.SceneName` retain
their C# spelling. An optional annotation argument supplies an explicit alias, for
example `[ScriptVisible("LegacyName")]`. Explicit invalid/empty names, Lua keywords,
ambiguous overloads and duplicate aliases are rejected. Renaming C# declarations
changes the script contract and requires a reviewed baseline update.

Only annotated instance properties/methods are bound. Each concrete subclass needs
its own `[ScriptType]`; that attribute is not inherited. Shared `[ScriptVisible]`
members are included only through a chain of explicitly annotated, non-ignored base
types. Members on unannotated bases remain hidden. `AllowWrite = true` opts a selected
public setter in; `ScriptIgnore` takes precedence. Constructors,
static members, fields, indexers, raw events, generic methods,
`GetType`, reflection objects, delegates and arbitrary collections are unavailable.
Arguments/results must be finite scalars, bounded UTF-8 strings, registered game enums or explicitly approved
annotated classes or structs. Nullable observations preserve `nil` rather than
inventing zero/false; `FirearmState` and motion snapshots use the same attributes.
Exact runtime types must be registered. A registered subclass returned through a base
declaration uses **only the declared base policy**, never its extra runtime members;
unknown subclasses still fail closed. Removing `[ScriptType]` prevents that exact
type from registering, and removing `[ScriptVisible]` hides the selected member.

Descriptors belong to one script and registry. They do not use MoonSharp's global
type registration or automatic CLR binding. Property access and callbacks check the
current execution budget before and after C# work. A C# method must itself have bounded
work and inputs: the interpreter cannot interrupt a blocking managed/native method
while that method is running.

The surface in [`ScriptAPI.Shipped.txt`](ScriptAPI.Shipped.txt) includes read-only
player, item/pickup/projectile, door, room, role, toy, hazard, generator, locker,
workstation, elevator, camera, ragdoll, tesla, effect, stat and voice metadata.
Native objects, encoded voice bytes, raw messages, collections and setters stay hidden.
Type discovery does not create a constructor or arbitrary static API. Host
instance globals are separate from type names; an object named `player` is accessed
as `player.PlayerId`. Tests pin this surface separately from the managed API baseline.

API-discovered sessions also receive `Game`, an annotated read-only local query object:

```lua
Events.Client.Ready.Add(function(args)
    local player = Game.LocalPlayer -- May still be nil at Ready.
    if player == nil or player.IsDestroyed then return end
    local item = Game:GetCurrentItem(player.PlayerId)
    local position = Game:GetPosition(player.PlayerId) -- Detached X/Y/Z.
    local health = Game:GetHealth(player.PlayerId) -- nil when not observable.
end)
```

`Game.GetPlayer(id)` resolves a player ID; `Game.GetPlayerAt(index)` uses a zero-based
index below `Game.PlayerCount`. `GetRoom`, `GetFirearmState`, `GetHumeShield` and
`GetStamina` query current client-known state. `SpectatedPlayer`, `RoundPhase`,
`IsRoundStarted` and `IsRoundEnded` are also available. Re-resolve objects after
role/despawn changes and check `IsDestroyed` before live reads. Native getters and
query bodies remain trusted CLR/IL2CPP work, not a hard interruptible boundary.
See [capabilities and next bindings](CAPABILITIES.md) for what scripts can change.

## Base-game enums

`DiscoverApi` also reads public enums from the known client `Assembly-CSharp` interop
assembly, including publicly reachable nested enums, plus the explicitly selected
shared `RoundPhase` contract. It exposes constants only;
enum owner classes, constructors and static methods remain unavailable. Discovery
does not scan other mods, Unity assemblies or server-provided assemblies.

```lua
local item = ItemType.KeycardJanitor
assert(item == Enums.ItemType.KeycardJanitor)
assert(RoleTypeId.ClassD == Enums["Il2CppPlayerRoles.RoleTypeId"].ClassD)
-- Nested/ambiguous names always have an exact CLR identity lookup:
local attack = Enums["Il2CppPlayerStatsSystem.Scp049DamageHandler+AttackType"]
```

Every enum has an `Enums["Full.Clr.Name"]` entry. A unique simple name also gets an
`Enums.Name` alias; public top-level enums get a bare `Name` global when the name is
unambiguous and unreserved. Nested enums do not expose their owner classes. All
registry nodes and constants are read-only. Duplicate short names have no short
alias, and their exact identities remain available. IL2CPP prefixes and `+` nested
separators in canonical keys match the loaded interop metadata.

Constants and enum-valued host results are canonical name strings, preserving
64-bit values without Lua number rounding. Annotated methods and writable properties
with registered enum types automatically accept those constants or case-insensitive
declared names, with surrounding whitespace allowed. Only already registered enum
types are admitted into an annotated type graph; enum registration does not expose
additional methods or properties. Multiple CLR names for the same value share one
canonical string (the first name in ordinal order), while input accepts every alias.

`[Flags]` inputs also accept comma-separated declared names, for example `"Read, Write"`,
up to 64 names and the existing 4096-byte string limit. Results use a deterministic
comma-separated combination of declared masks. Unnamed zero flags round-trip as
`"0"`; all other numeric strings, Lua numbers, unknown names and unknown flag bits
are rejected. Undefined non-flags results are rejected. Separate limits allow up to
1024 enum types, 2048 members per enum and 32768 members in total. Enum conversion
stays within the runtime's own bindings and does not install global MoonSharp converters.

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

The event registry uses category discovery and lazy subscriptions. Production discovery
inspects only the known API assembly and registers only public static events marked
`[ScriptEvent]`; an event's namespace or public visibility alone never opts it in.
`[ScriptIgnore]` wins. The coverage test compares discovery to the annotations, not
to all public events. Separate trusted fixture discovery can additionally restrict
the exact namespace. No arbitrary loaded assembly is scanned.
Names retain C# casing; `ClientEvents` supplies `Client`, `AssetEvents` supplies `Asset`,
and non-`Events` owner names stay unchanged: `Events.InputRegistry.Pressed` and
`Events.LocalPlayerMotion.Applied`. The optional event-name alias remains supported.
Only void zero/one-argument delegates and validated read-only data graphs are admitted.
`CameraManager.Changed` and `AnomalyMessaging.MessageReceived` are explicitly ignored:
their native camera/untyped message payloads are not annotated script data. There are
no owner-specific notification bypasses. `CanvasReady`, camera-shake and raw-network
event data intentionally omit unsafe native handles. Warhead detonation uses the
annotated `WarheadDetonatedEventArgs`, not unannotated `System.EventArgs`.

Lua subscribes with `Add(callback)` and removes by closure or opaque returned token.
Dot and colon calls are supported. The first subscription attaches one CLR delegate,
shared across sessions; the last removal detaches it. Removing a closure removes all
its registrations for that event/session; removing a token removes one. A removed
registration's queued deliveries remain invalid even if the same closure is added again.
The CLR event handler captures bounded read-only data and enqueues callbacks. It never
calls Lua. `Tick` admits work for two milliseconds, starting at most four deliveries
in repeated fair rounds; an active session can use otherwise idle capacity.
Each admitted callback has its own terminal wall allowance of at most ten milliseconds
(or a smaller configured timeout), not the frame's dwindling remainder. No new
callback starts after the scheduling window closes. Managed/native work and GC
cannot be hard-preempted, so this is not a hard two-millisecond frame ceiling.
Lua can define:

```lua
local function OnReady(args)
    -- args.SceneName is a frozen read-only value.
end

local token = Events.Client.Ready.Add(OnReady)
-- Events.Client.Ready.Remove(OnReady) or Events.Client.Ready.Remove(token)
```

Events are not replayed: initialization precedes Ready, but an earlier Connected,
CanvasReady or download event is not delivered retroactively. Disconnect and offer
replacement revoke queued work; do not rely on a Lua disconnect/unload callback.

Events marked `[ScriptEvent(CoalesceLatest = true)]` keep only the latest pending
snapshot for each registration generation. Continuous stats, voice and SCP-079
auxiliary-power observations use this policy. Coalescing is **per subscription,
not per player/stat/entity**: intervening samples, deltas and transition flags can
be skipped. Treat these as change notifications and re-query current state when
tracking several entities. Removal still invalidates pending work. Discrete events
and pack Messages remain ordered and do not coalesce; genuine reliable queue/byte
overflow still retires the session. Delivery policy is exported in JSON, the
reviewed baseline and LuaLS definitions.

Event data is copied before the publisher can mutate or reuse it. Snapshots allow
only approved read-only properties, exact runtime types, depth 8, 64 nodes, 4 KiB per
string and 16 KiB of accounted payload. They expose no methods, writes, reflection or
conversion back to live CLR arguments. Destroyed wrappers publish `IsDestroyed = true`
and `nil` for other properties instead of dereferencing dead native objects.
These are transport bounds, not a VM heap quota.
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
Runtime warnings include bounded source/event/reason context. Diagnostics have a
prepaid, four-message per-session limit so exhaustion cannot cause unbounded logging.
Use `Output:Log` for ordinary author diagnostics; it has a separate rate budget.

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

## Experimental allocation hardening

Each development session owns a reclaimable allocation quota, defaulting to
16 MiB (`AllocationLimitBytes`). It is installed before VM construction and retained
across parsing, invocation, event delivery, host argument conversion and coroutine
creation. Fresh CPU budgets do not reset it. Preflight reservations cover tables and
index growth, dynamic VM stack arrays, values, closures, argument arrays, source and
compiler overhead, string operations and audited temporary library buffers. Stacks
start at 64 slots and grow under the allowance up to their existing capacity limit.
Frozen event publications reserve a conservative 64 KiB per recipient before capture;
subscriptions, binding wrappers and queued work are accounted separately.
All sessions also charge one runtime-wide quota (64 MiB by default). A reservation
is charged before allocation and bound to the actual backing storage or a shared
retention owner. Bounded sweeps credit it only after the CLR has collected that owner.
Long weak references prevent early credits during finalization/resurrection. Retained
strings, aliases, nested event values, table capacity and escaped results remain charged.
Disposing/recreating sessions or revoking them does not refund still-live storage.
An aggregate denial retires the requesting session; healthy siblings can continue
using capacity that later becomes available.

Exhaustion produces terminal `MemoryLimit`, which bypasses Lua protected calls and
cannot be swallowed by an ordinary host callback. The session retires, drops queued
work and detaches its subscriptions without calling Lua. Failed reservations precede
large backing-array/string allocations. Source admission also reserves conservative
scratch overhead before parsing; this is an estimate, not a precise AST size model.

The registry reserves 256 bytes per tracked owner and caps tracking at 131,072 records
across the runtime. Pending reservations roll back if allocation fails; committed
reservations cannot be refunded by disposing their ticket. Unknown-lifetime charges
remain permanent. `MemoryChargedBytes` reports outstanding estimates, while
`MemoryTotalAllocatedBytes` records cumulative reservations and does not limit lifetime
work. Entry points and ticks sweep bounded batches; quota pressure can scan the bounded
registry. Production code never forces game-wide GC. Delayed collection or conservative
capacity/source estimates can therefore still reject a valid session. Small quotas
may reject VM setup. Neither counter measures actual live CLR heap or process RSS.

The host opts into reclaimable mode; direct engine callers retain the explicit
cumulative default. Isolated workers exercise 10,000 bounded engine calls and 10,000
actual host invocations with collection between batches, exceeding a lifetime allowance
while keeping outstanding charges bounded. Retention and resurrection fixtures check
that live storage cannot regain quota. Test collection does not guarantee natural GC
timing in Unity; the production remote worker may collect only its own process.

The in-process host remains unqualified for hostile-source memory containment.
Arbitrary CLR allocations inside an approved method cannot be intercepted by a VM
allowance. Remote packs therefore execute in a separate framework-dependent .NET 6
x64 worker, never a game-process VM. The worker references only the private engine
and framework; bounded data-only RPC preserves the shipped read-only gameplay surface,
Output, Events, Messages and Enums without exposing CLR/native objects.
The host verifies an embedded SHA256 identity for the worker DLL, deps, runtimeconfig
and single adjacent engine DLL before launch. A mandatory low-privilege AppContainer
(LPAC), never a standard-AppContainer fallback, is created suspended, assigned and
verified in a kill-on-close job before resume: 256 MiB process/job memory, one process,
10% CPU, and a 128 MiB CLR heap hard limit. Before resume, qualification requires the
LPAC creation attribute and actual `AccessCheck` results on a duplicated child token:
read access with User + ALL APPLICATION PACKAGES must deny, while User +
ALL RESTRICTED APPLICATION PACKAGES must allow. Zero capabilities and absence of
loopback exemptions are independently verified. A bounded audit of protected descendants
rejects explicit ALL APPLICATION PACKAGES / ALL RESTRICTED APPLICATION PACKAGES grants;
an owner-only protected DACL is permitted. A named cross-process lock coordinates
ACL changes, confined to explicit payload files, protected UserData and private temp,
without ancestor ACL changes. Owned cleanup is tracked and drained rather than
silently abandoned.
Filesystem/child-process denial, network isolation and native-memory-limit checks
must qualify before availability is published. Network results distinguish explicit
denial, timeout and Winsock unavailability. Windows Filtering Platform may drop a
denied connection rather than return an immediate error. A timeout, or the specifically
recognized Winsock startup error 10107, is accepted only together with parent-owned
positive controls against the same known listener before and after, a secret 32-byte
nonce, no child/other accepted connections, and independently verified LPAC, zero
capabilities and no loopback exemption. Error 10107 is reported as network unavailable,
not permission denial; its cause is not established by that code. The authority is
Windows' verified no-network-capability LPAC contract, never the error or timeout alone.
Host RPC/native-string/hierarchy/registry work and queues are independently bounded.
A dedicated watchdog thread, independent of ThreadPool availability, enforces the
five-second command wall limit and kills/revokes an unhealthy set. Aggregate required-pack
loading is transactional and async; stale connection/offer/activation scopes cannot
publish readiness.
Consent cannot bypass failed isolation, and no managed OOM recovery is claimed.

[`ALLOCATION-AUDIT.md`](ALLOCATION-AUDIT.md) records the reviewed CLR/compiler/error
and host allocation paths, new preflight/retention regressions, and remaining trusted
C#/runtime/native boundaries. In particular, an approved C# method body can allocate
outside VM instrumentation; a finite regression demonstrates that limitation.

## Connection readiness and execution

[`CONNECTION-LIFECYCLE.md`](CONNECTION-LIFECYCLE.md) records the authenticated connection
bootstrap, exact consent before bytes, fixed admission deadline, native/public readiness
barriers, generation replacement and revocation order. Download/validation no longer
depends on a player hub. Declining required assets closes the connection. Readiness
requires both initialized content and the authenticated server confirmation, plus the
facility. Script-pack validation accepts only declared, bounded strict UTF-8 `.lua` sources
with exact hashes and byte counts. AssetBundle validation remains data-only.
Script top-level initialization finishes before the client sends Activated. Failed or
declined script initialization closes the connection and revokes the entire set.

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

Windows x64 live qualification on 2026-10-26 exercised two authenticated dedicated-server
connections through ModLaunch: exact transferred source hashes, separate content/execution
consent, held native/public Ready, frozen events, retained player/native queries, typed
GetState/State messages, independent literal output with clear/expiry, 48 repeated query
cycles, fresh reconnect activations/workers, and worker exit/output revocation on both
disconnects. The real IL2CPP string conversion rejected 4097 UTF-16 characters before
managed copying inside the remote scope and succeeded outside it. This is not a blanket
qualification of arbitrary game/native method bodies or an in-process heap ceiling.

Reproduce with the opt-in [remote scripting smoke](../tools/EarlyAccessUiSmoke/README.md#remote-lua-worker-and-actual-native-api-scenario).
Choose an unused loopback endpoint: a new server identity at an already pinned address is
correctly rejected rather than replacing the owner's trust pin. Runtime archives include
the three worker payloads, their host-embedded identities, and the single private engine;
the release gates also compile the packaged client/server starters and verify their exact
embedded descriptor/script resource names.
