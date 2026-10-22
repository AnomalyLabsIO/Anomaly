using System.Reflection;
using HarmonyLib;
using MelonLoader;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace Anomaly.Bootstrap;

internal static partial class RuntimeGuard
{
    private static readonly RevisionWorkQueue<MethodBase> TargetWork = new(16384);
    private static readonly object RequiredSync = new();
    private static readonly Dictionary<MethodBase, List<RequiredPatch>> RequiredPatches = new();
    private static long _requiredRevision;
    private static long _operationRevision;
    private static int _verificationOperations;
    private static int _working;
    private static int _source;
    private static long _nextChecks;
    private static VerificationPass? _pass;
    private static TargetAudit? _targetAudit;
    private static Completion? _completion;
    private static long _completedChecks;
    private static long _checkpointReuses;
    private static long _auditedPatchRecords;
    internal static int LastSliceItems { get; private set; }
    internal static int LastSliceOverruns { get; private set; }
    internal static long CompletedChecks => Interlocked.Read(ref _completedChecks);
    internal static long CheckpointReuses => Interlocked.Read(ref _checkpointReuses);
    internal static long AuditedPatchRecords => Interlocked.Read(ref _auditedPatchRecords);

    private readonly record struct RequiredPatch(MethodInfo Method, HarmonyPatchType Kind, string? Owner);
    private readonly record struct WorkStamp(long Catalog, long Trust, long Native, long Targets, long Required, long Operations, long PendingNative);
    private sealed record Completion(WorkStamp Stamp, bool Complete, long Tick, VerificationPass Pass);

    internal static void EnqueueTargetAudit(MethodBase target)
    {
        // Deliberately getter-free: catalog registration also accepts generated
        // methods whose metadata is not yet safe to inspect.
        if (!TargetWork.Enqueue(target))
            ReportCoverageGap("Protected target audit queue capacity exceeded.");
    }

    private static void QueueProtectedTarget(MethodBase target)
    {
        if (ProtectedMethodCatalog.Classify(target) != ProtectionScope.None)
            EnqueueTargetAudit(target);
    }

    internal static void RegisterRequiredClientPatch(MethodBase target, MethodInfo patch, HarmonyPatchType kind, string owner)
    {
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("A required client patch needs its actual Harmony owner.", nameof(owner));
        RegisterRequiredPatch(target, new RequiredPatch(patch, kind, owner));
    }

    private static void RegisterRequiredPatch(MethodBase target, RequiredPatch patch)
    {
        lock (RequiredSync)
        {
            if (!RequiredPatches.TryGetValue(target, out var list)) RequiredPatches.Add(target, list = new());
            if (list.Contains(patch)) return;
            list.Add(patch);
            ++_requiredRevision;
        }
    }

    private static void InitializeVerification()
    {
        foreach (var hook in Hooks)
        {
            RegisterRequiredPatch(hook.Target, new RequiredPatch(hook.Prefix, HarmonyPatchType.Prefix, null));
            RegisterRequiredPatch(hook.Target, new RequiredPatch(hook.Finalizer, HarmonyPatchType.Finalizer, null));
        }
        foreach (var hook in RequiredStockHooks)
            RegisterRequiredPatch(hook.Target, new RequiredPatch(hook.Prefix, HarmonyPatchType.Prefix, null));
    }

    private static WorkStamp CurrentStamp()
    {
        var catalog = ProtectedMethodCatalog.CaptureRevisions();
        return new WorkStamp(catalog.Catalog, catalog.Trust, catalog.Native, TargetWork.Revision,
            Interlocked.Read(ref _requiredRevision), Interlocked.Read(ref _operationRevision), DeferredNative.Revision);
    }

    private static bool HasPendingWork => Volatile.Read(ref _verificationOperations) != 0 || TargetWork.Count != 0 ||
        ProtectedMethodCatalog.HasPendingWork || DeferredNative.HasPendingWork;

    private static bool VerifyCheckpoint(bool requireComplete)
    {
        if (!_installed) return false;
        if (!ClrEntryStubPreserver.IsInstalled)
        {
            Invalidate("Required CLR entry stub preservation was replaced.");
            return false;
        }
        var completed = Volatile.Read(ref _completion);
        bool current = completed != null && completed.Stamp == CurrentStamp() && !HasPendingWork &&
            (!requireComplete || completed.Complete) && Environment.TickCount64 < completed.Tick + 1000;
        // Workers never initialize metadata, inspect callbacks, JIT, or wait for a
        // main-thread checkpoint while potentially holding a transport/detour lock.
        if (!ProtectedMethodCatalog.IsMainThread)
            return current && Volatile.Read(ref _working) == 0 && completed!.Stamp == CurrentStamp() && !HasPendingWork && Snapshot.Healthy;
        if (current && completed!.Pass.FingerprintsUnchanged() && completed.Stamp == CurrentStamp() && !HasPendingWork)
        {
            Interlocked.Increment(ref _checkpointReuses);
            GuardTimingDiagnostics.CountTotal(GuardTimingTotal.CheckpointReuses);
            return Snapshot.Healthy;
        }
        if (Interlocked.CompareExchange(ref _working, 1, 0) != 0) return false;
        using var timing = GuardTimingDiagnostics.Begin(GuardTimingPhase.Checkpoint);
        GuardTimingDiagnostics.CountTotal(GuardTimingTotal.Checkpoints);
        try
        {
            ProtectedMethodCatalog.ResolveLoadedAssemblies();
            if (requireComplete) ProtectedMethodCatalog.RefreshNativeTargetsIfReady();
            _pass = new VerificationPass(CurrentStamp());
            // Only startup/admission boundaries drain synchronously. There is no
            // global inventory discovery here; normal frames use the same cursors.
            int remainingItems = 1_000_000;
            for (int attempt = 0; attempt < 8; ++attempt)
            {
                while (ProcessOneVerificationItem())
                {
                    if (--remainingItems == 0)
                    {
                        ReportCoverageGap("Guard checkpoint work limit exceeded; pending work remains unverified.");
                        return false;
                    }
                }
                if (Volatile.Read(ref _verificationOperations) != 0) return false;
                if (PublishCompletion(requireComplete)) return Snapshot.Healthy;
            }
            ReportCoverageGap("Guard checkpoint could not establish a stable work revision.");
            return false;
        }
        catch (Exception ex)
        {
            Invalidate("Guard checkpoint failed: " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
        finally { Volatile.Write(ref _working, 0); }
    }

    internal static void Update(Func<bool>? emitDiagnostic = null)
    {
        if (!_installed || !ProtectedMethodCatalog.IsMainThread || Interlocked.CompareExchange(ref _working, 1, 0) != 0) return;
        using var timing = GuardTimingDiagnostics.Begin(GuardTimingPhase.GuardSlice);
        var budget = new GuardWorkBudget();
        int warnings = 0;
        try
        {
            FailAbandonedIlHookApplications();
            if (!ClrEntryStubPreserver.IsInstalled)
            {
                Invalidate("Required CLR entry stub preservation was replaced.");
                return;
            }
            while (budget.CanStartItem())
            {
                if (_pass == null && Environment.TickCount64 >= _nextChecks)
                {
                    _pass = new VerificationPass(CurrentStamp());
                }
                else if (warnings < 4 && emitDiagnostic?.Invoke() == true)
                {
                    ++warnings;
                }
                else if (warnings < 4 && PatchPerformanceDiagnostics.TryDequeueWarning(out string warning))
                {
                    ++warnings;
                    MelonLogger.Warning(warning);
                }
                else if (!ProcessOneVerificationItem())
                {
                    if (_pass?.Done != true || HasPendingWork) break;
                    PublishCompletion(requireComplete: true);
                }
                budget.CompleteItem();
            }
        }
        catch (Exception ex)
        {
            Invalidate("Guard heartbeat failed: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            LastSliceItems = budget.Items;
            LastSliceOverruns = budget.Overruns;
            GuardTimingDiagnostics.RecordSlice(budget.Items, budget.Overruns);
            Volatile.Write(ref _working, 0);
        }
    }

    private static bool ProcessOneVerificationItem()
    {
        for (int attempt = 0; attempt < 5; ++attempt)
        {
            int source = _source;
            _source = (_source + 1) % 5;
            if (source == 0 && _pass != null && !_pass.Done) { _pass.Step(); return true; }
            if (source == 1 && ProtectedMethodCatalog.ProcessOnePendingWork()) return true;
            if (source == 2 && DeferredNative.ProcessOnePendingWork()) return true;
            if (source == 3 && ProcessTargetItem()) return true;
            if (source == 4 && AdmissionInventoryLedger.ProcessOneHeartbeatItem()) return true;
        }
        return false;
    }

    private static bool PublishCompletion(bool requireComplete)
    {
        if (_pass == null || !_pass.Done || HasPendingWork) return false;
        WorkStamp stamp = CurrentStamp();
        if (stamp != _pass.Stamp)
        {
            _pass = new VerificationPass(stamp);
            // A later main-thread checkpoint can drain this pass; a frame never
            // publishes an invariant result captured before a relevant mutation.
            return false;
        }
        bool complete = ProtectedMethodCatalog.RequiredCoverageComplete;
        if (requireComplete && !complete)
            foreach (string gap in ProtectedMethodCatalog.GetCoverageGaps()) ReportCoverageGap(gap);
        if (stamp != CurrentStamp() || HasPendingWork)
        {
            _pass = new VerificationPass(CurrentStamp());
            return false;
        }
        // A complete assessment may report bounded coverage gaps to permissive
        // admission. A partial startup assessment cannot stand in for that check.
        Volatile.Write(ref _completion, new Completion(stamp, requireComplete || complete, Environment.TickCount64, _pass));
        _pass = null;
        _nextChecks = Environment.TickCount64 + 1000;
        Interlocked.Increment(ref _completedChecks);
        GuardTimingDiagnostics.CountTotal(GuardTimingTotal.CompletedChecks);
        return true;
    }

    private static bool ProcessTargetItem()
    {
        if (_targetAudit == null)
        {
            if (!TargetWork.TryTake(out MethodBase target, out long version)) return false;
            _targetAudit = new TargetAudit(target, version);
            return true;
        }
        if (!TargetWork.IsCurrent(_targetAudit.Target, _targetAudit.Version) || _targetAudit.Step())
        {
            TargetWork.Complete(_targetAudit.Target, _targetAudit.Version);
            _targetAudit = null;
        }
        return true;
    }

    private sealed class PatchCursor
    {
        internal readonly Patch[][] Arrays;
        private int _kind, _index;
        internal PatchCursor(MethodBase target) { Arrays = GuardRuntimeAdapters.CapturePatches(target); }
        internal bool TryNext(out Patch patch, out HarmonyPatchType kind)
        {
            while (_kind < Arrays.Length)
            {
                if (_index < Arrays[_kind].Length)
                {
                    patch = Arrays[_kind][_index++];
                    kind = _kind switch { 0 => HarmonyPatchType.Prefix, 1 => HarmonyPatchType.Postfix,
                        2 => HarmonyPatchType.Transpiler, 3 => HarmonyPatchType.Finalizer, _ => HarmonyPatchType.ILManipulator };
                    return true;
                }
                ++_kind;
                _index = 0;
            }
            patch = null!; kind = default; return false;
        }
    }

    private sealed class VerificationPass
    {
        internal readonly WorkStamp Stamp;
        private readonly (MethodBase Target, RequiredPatch[] Required)[] _targets;
        private readonly List<(MethodBase Target, Patch[][] Arrays)> _fingerprints = new();
        private readonly Delegate?[] _callbacks = new Delegate?[Callbacks.Count];
        private int _target, _callback;
        private PatchCursor? _cursor;
        private bool[]? _seen;
        private ProtectionScope _scope;
        internal bool Done => _target == _targets.Length && _callback == Callbacks.Count;
        internal VerificationPass(WorkStamp stamp)
        {
            Stamp = stamp;
            lock (RequiredSync) _targets = RequiredPatches.Select(pair => (pair.Key, pair.Value.ToArray())).ToArray();
        }
        internal void Step()
        {
            if (_target < _targets.Length)
            {
                var target = _targets[_target];
                if (_cursor == null)
                {
                    _scope = ProtectedMethodCatalog.Classify(target.Target);
                    _cursor = new PatchCursor(target.Target);
                    _fingerprints.Add((target.Target, _cursor.Arrays));
                    _seen = new bool[target.Required.Length];
                }
                else if (_cursor.TryNext(out Patch patch, out HarmonyPatchType kind))
                {
                    for (int i = 0; i < target.Required.Length; ++i)
                    {
                        var required = target.Required[i];
                        if (kind == required.Kind && patch.PatchMethod == required.Method &&
                            (required.Owner == null || StringComparer.Ordinal.Equals(required.Owner, patch.owner))) _seen![i] = true;
                    }
                    AuditPatchRecord(target.Target, _scope, patch, kind);
                }
                else
                {
                    for (int i = 0; i < target.Required.Length; ++i)
                        if (!_seen![i]) Invalidate("Required interceptor missing: " + Describe(target.Target) + " <- " + Describe(target.Required[i].Method));
                    ++_target; _cursor = null; _seen = null;
                }
                return;
            }
            if (_callback == Callbacks.Count) return;
            var slot = Callbacks[_callback];
            GuardTimingDiagnostics.CountTotal(GuardTimingTotal.CallbackChecks);
            Delegate? value = (Delegate?)slot.Field.GetValue(null);
            _callbacks[_callback++] = value;
            Delegate[] current = value?.GetInvocationList() ?? Array.Empty<Delegate>();
            if (current.Length == 0 || !current[0].Equals(slot.Guard) ||
                slot.Previous.Where(IsStockShieldCallback).Any(prior => !current.Contains(prior)))
                Invalidate("Guard or loader callback removed/reordered: " + slot.Field.DeclaringType?.Name + "." + slot.Field.Name);
            if (slot.Field.Name == "OnDetour" && slot.Field.DeclaringType != typeof(NativeDetour) && !current.Any(IsStockShieldCallback))
                Invalidate("Required loader PatchShield detour callback missing: " + slot.Field.DeclaringType?.Name);
        }
        internal bool FingerprintsUnchanged()
        {
            foreach (var entry in _fingerprints)
            {
                Patch[][] arrays = GuardRuntimeAdapters.CapturePatches(entry.Target);
                if (arrays.Length != entry.Arrays.Length) return false;
                for (int i = 0; i < arrays.Length; ++i)
                    if (!ReferenceEquals(arrays[i], entry.Arrays[i])) return false;
            }
            for (int i = 0; i < _callbacks.Length; ++i)
                if (!ReferenceEquals(Callbacks[i].Field.GetValue(null), _callbacks[i])) return false;
            return true;
        }
    }

    private static bool AuditPatchRecord(MethodBase target, ProtectionScope scope, Patch patch, HarmonyPatchType kind)
    {
        Interlocked.Increment(ref _auditedPatchRecords);
        GuardTimingDiagnostics.CountTotal(GuardTimingTotal.PatchRecords);
        if (scope == ProtectionScope.None || ProtectedMethodCatalog.IsTrustedPatch(target, patch.PatchMethod, kind)) return true;
        _audit.Record(GuardAuditFlags.ExternalHarmonyModification,
            "Existing critical patch: " + Describe(target) + " <- " + Describe(patch.PatchMethod),
            fatal: scope is ProtectionScope.Core or ProtectionScope.Infrastructure);
        return false;
    }

    private sealed class TargetAudit
    {
        internal readonly MethodBase Target;
        internal readonly long Version;
        private readonly ProtectionScope _scope;
        private readonly PatchCursor _patches;
        private int _stage, _index;
        private MethodBase? _chainTarget;
        private bool _allTrusted = true, _hasPatches;
        internal TargetAudit(MethodBase target, long version)
        {
            GuardTimingDiagnostics.CountTotal(GuardTimingTotal.TargetAudits);
            Target = target; Version = version;
            _scope = ProtectedMethodCatalog.Classify(target);
            _patches = new PatchCursor(target);
        }
        internal bool Step()
        {
            if (_scope == ProtectionScope.None) return true;
            if (_stage == 0)
            {
                if (_patches.TryNext(out Patch patch, out HarmonyPatchType kind))
                { _hasPatches = true; _allTrusted &= AuditPatchRecord(Target, _scope, patch, kind); return false; }
                ++_stage;
                return false;
            }
            if (_stage > 2) return true;
            object? item = GuardRuntimeAdapters.ReadChainItem(_chainTarget ??= Target.GetIdentifiable(), _stage == 2, _index++, out bool end);
            if (end) { ++_stage; _index = 0; return false; }
            if (item != null)
                GuardTimingDiagnostics.CountTotal(_stage == 2 ? GuardTimingTotal.IlHookRecords : GuardTimingTotal.ManagedDetourRecords);
            if (item == null || !GuardRuntimeAdapters.IsChainItemApplied(item)) return false;
            lock (Sync) if (OwnedDetours.ContainsKey(item)) return false;
            if (IsSupportedRuntimeDetour(item, Target) || (_hasPatches && _allTrusted && IsHarmonyBackend(item, Target)))
            {
                lock (Sync) OwnedDetours[item] = Target;
                ProtectBackendReplacement(item);
            }
            else
            {
                lock (Sync) ObservedDetours[item] = Target;
                _audit.Record(GuardAuditFlags.ExternalDetourModification, "Existing critical detour: " + Describe(Target),
                    fatal: _scope is ProtectionScope.Core or ProtectionScope.Infrastructure);
            }
            return false;
        }
    }
}
