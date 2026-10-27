using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Events;
using Anomaly.ScriptingCpuProbe.SnapshotFixtures;
using MoonSharp.Interpreter;

namespace Anomaly.ScriptingCpuProbe;

internal static partial class Program
{
    // Forced collections are deliberately confined to this isolated regression
    // worker. Production Sweep observes CLR collection; it does not request one.
    private static void ExecuteReclaimable(string fixture, ProbeState state)
    {
        switch (fixture)
        {
            case "reclaimable-bounded-entries":
                ReclaimableScriptCalls(state, retainedKind: null);
                break;
            case "reclaimable-host-session":
                ReclaimableHostSession(state);
                break;
            case "reclaimable-value-assignment":
                ReclaimableValueAssignment(state);
                break;
            case "reclaimable-nested-event-snapshot":
                ReclaimableNestedEventSnapshot(state);
                break;
            case "reclaimable-noop-event-remove":
                ReclaimableNoopEventRemoval(state);
                break;
            case "reclaimable-retained-strings":
            case "reclaimable-retained-tables":
            case "reclaimable-retained-closures":
            case "reclaimable-retained-prime-tables":
                ReclaimableScriptCalls(state, fixture);
                break;
            case "reclaimable-unbound-rollback":
            {
                state.Memory = new MemoryBudget(4096, reclaimable: true);
                for (var index = 0; index < 1000; index++)
                {
                    var ticket = state.Memory.ReserveAllocation(512);
                    ReclaimCheck(state, state.Memory.ChargedBytes >= 512, "Unbound allocation was not reserved.");
                    ticket.Dispose();
                    ticket.Dispose();
                    ReclaimCheck(state, state.Memory.ChargedBytes == 0 && state.Memory.UnboundBytes == 0,
                        "An unbound rollback leaked or credited twice.");
                }
                break;
            }
            case "reclaimable-throwing-factory":
            {
                state.Memory = new MemoryBudget(4096, reclaimable: true);
                using (state.Memory.EnterScope())
                {
                    try { MemoryBudget.Allocate<byte[]>(1024, () => throw new InvalidOperationException("fixed factory failure")); }
                    catch (InvalidOperationException) { state.RejectedCalls++; }
                }
                ReclaimCheck(state, state.Memory.ChargedBytes == 0 && state.Memory.UnboundBytes == 0,
                    "A failed backing allocation did not roll back its reservation.");
                break;
            }
            case "reclaimable-merged-owner-tickets":
            {
                state.Memory = new MemoryBudget(8192, reclaimable: true);
                var witness = RetainMergedOwnerTickets(state);
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, witness.IsAlive && state.Memory.ChargedBytes >= 768,
                    "Merging bound reservations credited their still-live backing storage.");
                state.RetainedOwner = null;
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, !witness.IsAlive && state.Memory.ChargedBytes == 0 && state.Memory.Sweep(4096) == 0,
                    "Merged owner reservations were lost or credited twice.");
                break;
            }
            case "reclaimable-sweep-observation-only":
            {
                state.Memory = new MemoryBudget(8192, reclaimable: true);
                _ = BindAndDisposeTicket(state, state.Memory, 1024);
                var reserved = state.Memory.ChargedBytes;
                var collections = GC.CollectionCount(GC.MaxGeneration);
                var ambient = MemoryBudget.Current;
                for (var index = 0; index < 64; index++)
                    ReclaimCheck(state, state.Memory.Sweep(4096) == 0, "Sweep reclaimed a live owner.");
                ReclaimCheck(state, GC.CollectionCount(GC.MaxGeneration) == collections && ReferenceEquals(ambient, MemoryBudget.Current),
                    "Sweep forced global collection or changed the ambient budget.");
                ReclaimCheck(state, state.Memory.ChargedBytes == reserved, "Observation-only sweeping changed live allocation credit.");
                state.RetainedOwner = null;
                ForceReclaim(state.Memory, state);
                break;
            }
            case "reclaimable-bound-dispose":
            {
                state.Memory = new MemoryBudget(4096, reclaimable: true);
                var witness = BindAndDisposeTicket(state, state.Memory, 512);
                var reserved = state.Memory.ChargedBytes;
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, witness.IsAlive && state.Memory.ChargedBytes == reserved && reserved >= 512,
                    "Disposing a bound ticket credited live storage.");
                state.RetainedOwner = null;
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, !witness.IsAlive && state.Memory.ChargedBytes == 0,
                    "Dead storage was not reclaimed.");
                for (var index = 0; index < 4; index++)
                    ReclaimCheck(state, state.Memory.Sweep(4096) == 0 && state.Memory.ChargedBytes == 0,
                        "Repeated sweep credited an allocation twice.");
                break;
            }
            case "reclaimable-abandoned-child-retained-owner":
            {
                state.Memory = new MemoryBudget(8192, reclaimable: true);
                var witness = BindAbandonedChild(state, state.Memory);
                var reserved = state.Memory.ChargedBytes;
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, witness.IsAlive && state.Memory.ChargedBytes == reserved && reserved >= 1024,
                    "Dropping a child budget credited its still-live backing object.");
                state.RetainedOwner = null;
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, !witness.IsAlive && state.Memory.ChargedBytes == 0,
                    "The root registry lost ownership of an abandoned child's allocation.");
                break;
            }
            case "reclaimable-shared-string-clone":
            {
                state.Memory = new MemoryBudget(64 * 1024, reclaimable: true);
                var witness = RetainStringClone(state);
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, witness.IsAlive && state.Memory.ChargedBytes >= 32 + 2 * 4096,
                    "Dropping a DynValue credited string storage retained by its clone.");
                state.RetainedOwner = null;
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, !witness.IsAlive && state.Memory.ChargedBytes == 0,
                    "The final string/clone did not become reclaimable.");
                break;
            }
            case "reclaimable-cleared-table-capacity":
            {
                state.Memory = new MemoryBudget(2 * 1024 * 1024, reclaimable: true);
                var witness = RetainClearedTable(state);
                ForceReclaim(state.Memory, state);
                // Dictionary.Clear retains the bucket/entry arrays in this engine.
                ReclaimCheck(state, witness.IsAlive && state.Memory.ChargedBytes >= 256 * 8,
                    "Clearing a live table credited retained dictionary capacity.");
                state.RetainedOwner = null;
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, !witness.IsAlive && state.Memory.ChargedBytes == 0,
                    "Dead table capacity was not reclaimed.");
                break;
            }
            case "reclaimable-pinned-owner":
            {
                state.Memory = new MemoryBudget(8192, reclaimable: true);
                var pin = AllocatePinned(state.Memory);
                try
                {
                    var reserved = state.Memory.ChargedBytes;
                    ForceReclaim(state.Memory, state);
                    ReclaimCheck(state, state.Memory.ChargedBytes == reserved && reserved >= 1024,
                        "Pinned backing storage was credited while still alive.");
                }
                finally { pin.Free(); }
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, state.Memory.ChargedBytes == 0, "Released pinned storage was not reclaimed.");
                break;
            }
            case "reclaimable-resurrected-owner":
            {
                state.Memory = new MemoryBudget(8192, reclaimable: true);
                TrackResurrectable(state.Memory);
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, _resurrectedOwner != null && state.Memory.ChargedBytes >= 1024,
                    "Short weak ownership credited storage resurrected by its finalizer.");
                _resurrectedOwner = null;
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, state.Memory.ChargedBytes == 0, "Dead resurrected owner remained charged.");
                break;
            }
            case "reclaimable-parent-failure-reusable":
                ReusableParentAfterChildFailure(state);
                break;
            default: throw new ArgumentException("Unknown fixed reclaimable-allocation fixture.", nameof(fixture));
        }
    }

    private static void ReclaimableScriptCalls(ProbeState state, string? retainedKind)
    {
        const CoreModules modules = CoreModules.GlobalConsts | CoreModules.TableIterators | CoreModules.Basic |
            CoreModules.Math | CoreModules.Table | CoreModules.String | CoreModules.ErrorHandling;
        state.Memory = new MemoryBudget(retainedKind == null ? 16L * 1024 * 1024 : 2L * 1024 * 1024, reclaimable: true);
        var script = new Script(modules, state.Memory);
        FreshCpuBudget(script, state);
        var body = retainedKind switch
        {
            null => "local value=string.rep('x',1024); return #value",
            "reclaimable-retained-strings" => "keep[#keep+1]=string.rep('x',1024); return #keep",
            "reclaimable-retained-tables" => "keep[#keep+1]={1,2,3,4,5,6,7,8}; return #keep",
            "reclaimable-retained-closures" => "local value=#keep+1; keep[value]=function() return value end; return #keep",
            "reclaimable-retained-prime-tables" => "keep[#keep+1]=${value=1}; return #keep",
            _ => throw new InvalidOperationException("Unsupported growth fixture.")
        };
        script.DoString("keep={}; function work() " + body + " end");
        var function = script.Globals.Get("work");
        ForceReclaim(state.Memory, state);
        var count = retainedKind == null ? 10000 : 5000;
        for (var index = 0; index < count; index++)
        {
            FreshCpuBudget(script, state);
            state.Number = script.Call(function).Number;
            state.Entries++;
            if ((index + 1) % 32 == 0) ForceReclaim(state.Memory, state);
        }
        ForceReclaim(state.Memory, state);
        GC.KeepAlive(script);
    }

    private static void ReclaimableHostSession(ProbeState state)
    {
        const long limit = 16L * 1024 * 1024;
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions
        {
            AllocationLimitBytes = limit,
            Timeout = TimeSpan.FromSeconds(1)
        });
        var loaded = session.Load("function work() local value=string.rep('x',1024); return #value end");
        ReclaimCheck(state, loaded.Succeeded, "Fixed host-session source failed to load: " + loaded.Status);
        for (var index = 0; index < 10000; index++)
        {
            var result = session.Invoke("work");
            ReclaimCheck(state, result.Succeeded, "A bounded host entry failed: " + result.Status);
            ReclaimCheck(state, result.Value is double number && number == 1024, "Host entry returned an unexpected value.");
            state.Entries++;
            state.Number = 1024;
            if ((index + 1) % 32 == 0)
            {
                CollectWorkerGarbage();
                // Exercise the actual production host maintenance path rather
                // than reaching into its private budget or forcing GC there.
                runtime.Tick();
            }
        }
        CollectWorkerGarbage();
        runtime.Tick();
        ReclaimCheck(state, !session.IsRetired, "The bounded host session retired.");
        ReclaimCheck(state, session.MemoryChargedBytes > 0 && session.MemoryChargedBytes <= limit,
            "The host session exceeded its outstanding allocation allowance.");
        ReclaimCheck(state, session.MemoryTotalAllocatedBytes > limit, "Host workload did not exceed one cumulative allowance.");
        ReclaimCheck(state, session.MemoryUnboundBytes == 0, "Host calls accumulated unknown-lifetime allocation debt.");
        ReclaimCheck(state, !ScriptingPolicy.MemoryHardeningQualified,
            "Local allocation accounting must not be represented as a qualified heap ceiling.");
        state.MemoryChargedOverride = session.MemoryChargedBytes;
        state.MemoryLimitOverride = limit;
        state.MemoryTotalOverride = session.MemoryTotalAllocatedBytes;
        state.MemoryUnboundOverride = session.MemoryUnboundBytes;
    }

    private static void ReclaimableValueAssignment(ProbeState state)
    {
        state.Memory = new MemoryBudget(16L * 1024 * 1024, reclaimable: true);
        CreateAssignedCell(state);
        WeakReference? last = null;
        for (var index = 0; index < 10000; index++)
        {
            last = AssignNewString(state);
            state.Entries++;
            if ((index + 1) % 32 == 0) ForceReclaim(state.Memory, state);
        }
        ForceReclaim(state.Memory, state);
        ReclaimCheck(state, last!.IsAlive && state.Memory.ChargedBytes >= 32 + 2 * 1024,
            "DynValue.Assign lost ownership of its live backing string.");
        ClearAssignedValue(state);
        ForceReclaim(state.Memory, state);
        ReclaimCheck(state, !last.IsAlive && state.Memory.ChargedBytes is > 0 and < 32 + 2 * 1024,
            "Overwriting the assigned value did not release the old backing string.");
        state.RetainedOwner = null;
        ForceReclaim(state.Memory, state);
        ReclaimCheck(state, state.Memory.ChargedBytes == 0, "Dead assigned-value wrapper stayed charged.");
    }

    private static void ReclaimableNestedEventSnapshot(ProbeState state)
    {
        SnapshotEvents.Reset();
        try
        {
            state.Memory = new MemoryBudget(16L * 1024 * 1024, reclaimable: true);
            var bindings = new ScriptBindingRegistry();
            using var catalog = ScriptEventCatalog.Discover(bindings, typeof(SnapshotEvents).Assembly,
                typeof(SnapshotEvents).Namespace!);
            var script = new Script(CoreModules.None, state.Memory);
            FreshCpuBudget(script, state);
            var queue = new List<ScriptEventInvocation>();
            using var subscriptions = new ScriptEventSubscriptions(catalog, script, queue.Add);
            var callback = script.LoadString("return 1");
            subscriptions.Add(catalog.Categories["Snapshot"]["Published"], callback);
            ForceReclaim(state.Memory, state);
            var baseline = state.Memory.ChargedBytes;
            var anchor = PublishAndRetainNestedSnapshot(state, queue);
            state.Entries = 1;
            ForceReclaim(state.Memory, state);
            var withChild = state.Memory.ChargedBytes;
            ReclaimCheck(state, anchor.IsAlive, "Dropping a frozen root lost the child snapshot's publication anchor.");
            ReclaimCheck(state, withChild >= baseline + 64 * 1024,
                "A retained nested snapshot did not keep its full publication reservation.");
            state.RetainedOwner = null;
            ForceReclaim(state.Memory, state);
            var withoutChild = state.Memory.ChargedBytes;
            ReclaimCheck(state, !anchor.IsAlive, "The final nested snapshot's publication anchor stayed alive.");
            ReclaimCheck(state, withChild - withoutChild >= 64 * 1024,
                "Releasing the last nested snapshot did not return its publication allowance.");
            state.Number = withChild - withoutChild;
            subscriptions.Dispose();
            ReclaimCheck(state, SnapshotEvents.Removes == 1, "The snapshot fixture did not detach its CLR event bridge.");
            GC.KeepAlive(callback);
            GC.KeepAlive(script);
            GC.KeepAlive(queue);
        }
        finally { SnapshotEvents.Reset(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PublishAndRetainNestedSnapshot(ProbeState state, List<ScriptEventInvocation> queue)
    {
        SnapshotEvents.Publish(new SnapshotPayload(new SnapshotPayload(null, "nested"), "root"));
        if (queue.Count != 1) throw new InvalidOperationException("The fixed event publication was not queued exactly once.");
        var root = (ScriptFrozenEventValue)queue[0].Arguments[0]!;
        var child = (ScriptFrozenEventValue)root.Fields[nameof(SnapshotPayload.Next)]!;
        if (child.AllocationOwner == null || !ReferenceEquals(root.AllocationOwner, child.AllocationOwner))
            throw new InvalidOperationException("Nested event data does not share its publication owner.");
        state.RetainedOwner = child;
        queue.Clear();
        return new WeakReference(child.AllocationOwner, trackResurrection: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateAssignedCell(ProbeState state)
    {
        using var scope = state.Memory!.EnterScope();
        state.RetainedOwner = DynValue.NewNil();
    }

    private static void ReclaimableNoopEventRemoval(ProbeState state)
    {
        state.Memory = new MemoryBudget(16L * 1024 * 1024, reclaimable: true);
        var bindings = new ScriptBindingRegistry();
        using var catalog = ScriptEventCatalog.Discover(bindings, typeof(SnapshotEvents).Assembly,
            typeof(SnapshotEvents).Namespace!);
        var script = new Script(CoreModules.None, state.Memory);
        FreshCpuBudget(script, state);
        using var subscriptions = new ScriptEventSubscriptions(catalog, script, _ => { });
        var source = catalog.Categories["Snapshot"]["Published"];
        for (var index = 0; index < 10000; index++)
        {
            using (state.Memory.EnterScope())
                ReclaimCheck(state, !subscriptions.Remove(source, DynValue.NewString("00000000000000000000000000000000")),
                    "A nonexistent event token was reported as removed.");
            state.Entries++;
            if ((index + 1) % 32 == 0) ForceReclaim(state.Memory, state);
        }
        ForceReclaim(state.Memory, state);
        ReclaimCheck(state, state.Memory.UnboundBytes == 0 && state.Memory.ChargedBytes < 1024 * 1024,
            "No-op event removal attached debt to an immortal empty-array singleton.");
        GC.KeepAlive(script);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AssignNewString(ProbeState state)
    {
        using var scope = state.Memory!.EnterScope();
        var text = MemoryBudget.AllocateString(1024, () => new string('x', 1024));
        ((DynValue)state.RetainedOwner!).Assign(DynValue.NewString(text));
        return new WeakReference(text, trackResurrection: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearAssignedValue(ProbeState state)
    {
        using var scope = state.Memory!.EnterScope();
        ((DynValue)state.RetainedOwner!).Assign(DynValue.NewNil());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BindAndDisposeTicket(ProbeState state, MemoryBudget budget, int bytes)
    {
        var ticket = budget.ReserveAllocation(bytes);
        var owner = new byte[bytes];
        ticket.Bind(owner);
        ticket.Dispose();
        ticket.Dispose();
        state.RetainedOwner = owner;
        return new WeakReference(owner, trackResurrection: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BindAbandonedChild(ProbeState state, MemoryBudget parent)
    {
        var child = new MemoryBudget(4096, parent, reclaimable: true);
        return BindAndDisposeTicket(state, child, 1024);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RetainMergedOwnerTickets(ProbeState state)
    {
        var first = state.Memory!.ReserveAllocation(512);
        var owner = new byte[1024];
        first.Bind(owner);
        var second = state.Memory.ReserveAllocation(256);
        second.Bind(owner);
        first.Dispose();
        second.Dispose();
        state.RetainedOwner = owner;
        return new WeakReference(owner, trackResurrection: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RetainStringClone(ProbeState state)
    {
        using var scope = state.Memory!.EnterScope();
        var text = MemoryBudget.AllocateString(4096, () => new string('x', 4096));
        var original = DynValue.NewString(text);
        state.RetainedOwner = original.Clone();
        return new WeakReference(text, trackResurrection: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RetainClearedTable(ProbeState state)
    {
        using var scope = state.Memory!.EnterScope();
        var table = new Table(null);
        for (var index = 1; index <= 256; index++) table.Set(index, DynValue.NewNumber(index));
        table.Clear();
        state.RetainedOwner = table;
        return new WeakReference(table, trackResurrection: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static GCHandle AllocatePinned(MemoryBudget budget)
    {
        using var scope = budget.EnterScope();
        var owner = MemoryBudget.Allocate(1024, () => new byte[1024]);
        return GCHandle.Alloc(owner, GCHandleType.Pinned);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TrackResurrectable(MemoryBudget budget)
    {
        var owner = new ResurrectableOwner();
        budget.Track(owner, 1024);
    }

    private static ResurrectableOwner? _resurrectedOwner;
    private sealed class ResurrectableOwner
    {
        // The finalizer deliberately keeps the actual backing owner alive again.
        ~ResurrectableOwner() => _resurrectedOwner = this;
    }

    private static void ReusableParentAfterChildFailure(ProbeState state)
    {
        var parent = new MemoryBudget(8192, reclaimable: true);
        var first = new MemoryBudget(8192, parent, reclaimable: true);
        _ = BindAndDisposeTicket(state, first, 6000);
        var second = new MemoryBudget(8192, parent, reclaimable: true);
        state.Memory = second;
        try { using var rejected = second.ReserveAllocation(4096); }
        catch (ScriptExecutionAbortedException error) when (error.Reason == ExecutionTerminationReason.MemoryLimit)
        { state.RejectedCalls++; }
        ReclaimCheck(state, second.IsTerminated && !parent.IsTerminated && !first.IsTerminated,
            "Aggregate denial poisoned a healthy sibling or the reusable pool.");
        ReclaimCheck(state, second.ChargedBytes == 0 && second.UnboundBytes == 0,
            "Failed parent reservation was not rolled back atomically.");
        first.Check();
        state.RetainedOwner = null;
        ForceReclaim(parent, state);
        ReclaimCheck(state, parent.ChargedBytes == 0, "Released first-child owner still consumes aggregate allowance.");
        var third = new MemoryBudget(8192, parent, reclaimable: true);
        _ = BindAndDisposeTicket(state, third, 1024);
        ReclaimCheck(state, !third.IsTerminated && parent.ChargedBytes >= 1024,
            "Released aggregate capacity was not reusable by a fresh child.");
        state.RetainedOwner = null;
        ForceReclaim(parent, state);
        state.ParentChargedBytes = parent.ChargedBytes;
        ReclaimCheck(state, parent.ChargedBytes == 0, "Aggregate capacity was credited incorrectly after reuse.");
        second.Check(); // Failure remains terminal even after the pool becomes available.
    }

    private static void ForceReclaim(MemoryBudget budget, ProbeState state)
    {
        CollectWorkerGarbage();
        // Sweep work is bounded even if an accounting regression loses dead entries.
        for (var index = 0; index < 32; index++) state.ReleasedRecords += budget.Sweep(4096);
    }

    private static void CollectWorkerGarbage()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static void ReclaimCheck(ProbeState state, bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        state.Checks++;
    }
}
