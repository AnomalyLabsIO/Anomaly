using System.Reflection;
using HarmonyLib;
using HostHarmony = HarmonyLib.Harmony;

namespace Anomaly.Bootstrap;

// Advisory observations only. These values never participate in admission or
// guard flags, and failures here must not change the intercepted operation.
internal static class PatchPerformanceDiagnostics
{
    private static readonly PatchPerformanceTracker Tracker = new();
    private static readonly FieldInfo? DirectInstance = FindField(typeof(PatchProcessor), "instance");
    private static readonly FieldInfo? ClassInstance = FindField(typeof(PatchClassProcessor), "instance");
    private static readonly FieldInfo? ClassReplacement = FindClassReplacement();
    private static readonly object? HarmonyLocker = ReadLocker();

    internal static string? GetProcessorOwner(object processor)
    {
        try
        {
            FieldInfo? field = processor is PatchProcessor ? DirectInstance :
                processor is PatchClassProcessor ? ClassInstance : null;
            return (field?.GetValue(processor) as HostHarmony)?.Id;
        }
        catch { return null; }
    }

    internal static bool IsClassInstallationCompleted(object job)
    {
        try { return ClassReplacement?.GetValue(job) is MethodInfo; }
        catch { return false; }
    }

    internal static void CompleteHarmonyOperation(MethodBase target,
        (MethodInfo Method, HarmonyPatchType Kind, string Owner, int Index)[] before,
        bool installationCompleted)
    {
        try
        {
            if (HarmonyLocker == null) return;
            // No guard/catalog locks may be held by this caller. Harmony's own
            // lock orders the current metadata observation and tracker update.
            lock (HarmonyLocker)
            {
                Patches? patches = HostHarmony.GetPatchInfo(target);
                var after = new List<(MethodInfo, HarmonyPatchType, string, int)>();
                if (patches != null)
                {
                    Add(after, patches.Prefixes, HarmonyPatchType.Prefix);
                    Add(after, patches.Postfixes, HarmonyPatchType.Postfix);
                    Add(after, patches.Transpilers, HarmonyPatchType.Transpiler);
                    Add(after, patches.Finalizers, HarmonyPatchType.Finalizer);
                    Add(after, patches.ILManipulators, HarmonyPatchType.ILManipulator);
                }
                Tracker.ObserveCompletedOperation(target, before, after.ToArray(),
                    installationCompleted, Environment.TickCount64);
            }
        }
        catch { /* Optional diagnostics cannot affect Harmony or guard control flow. */ }
    }

    internal static bool TryDequeueWarning(out string warning)
    {
        try { return Tracker.TryDequeueWarning(out warning); }
        catch { warning = ""; return false; }
    }

    private static void Add(List<(MethodInfo, HarmonyPatchType, string, int)> result,
        IEnumerable<Patch> patches, HarmonyPatchType kind)
    {
        foreach (Patch patch in patches)
            result.Add((patch.PatchMethod, kind, patch.owner, patch.index));
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        try { return type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic); }
        catch { return null; }
    }

    private static object? ReadLocker()
    {
        try { return FindField(typeof(PatchProcessor), "locker")?.GetValue(null); }
        catch { return null; }
    }

    private static FieldInfo? FindClassReplacement()
    {
        try
        {
            Type? jobType = typeof(PatchClassProcessor).GetMethod("ProcessPatchJob",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetParameters().FirstOrDefault()?.ParameterType;
            return jobType == null ? null : jobType.GetField("replacement",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch { return null; }
    }
}

// The adapter supplies one ordered, authoritative post-operation metadata view.
// Before snapshots identify transitions but are not a transaction: concurrent
// or nested operations can hide brief removals, so churn deliberately undercounts.
internal sealed class PatchPerformanceTracker
{
    internal const int MaximumTrackedTuples = 4096;
    internal const int MaximumQueuedWarnings = 128;
    internal const long ChurnWindowMilliseconds = 5 * 60 * 1000;
    private const int MaximumOwnerLength = 1024;
    private readonly object _gate = new();
    private readonly Dictionary<PatchKey, TrackedPatch> _tracked = new();
    private readonly Queue<string> _warnings = new();
    private long _suppressedWarnings;
    private bool _trackingCapacityReached;
    private bool _trackingCapacityReported;

    internal int TrackedTupleCount { get { lock (_gate) return _tracked.Count; } }
    internal int PendingWarningCount { get { lock (_gate) return _warnings.Count; } }

    internal void ObserveCompletedOperation(MethodBase target,
        (MethodInfo Method, HarmonyPatchType Kind, string Owner, int Index)[] before,
        (MethodInfo Method, HarmonyPatchType Kind, string Owner, int Index)[] after,
        bool installationCompleted, long now)
    {
        lock (_gate)
        {
            var observations = new Dictionary<PatchKey, Counts>();
            Count(target, before, observations, beforeOperation: true);
            Count(target, after, observations, beforeOperation: false);
            foreach (var observation in observations)
            {
                PatchKey key = observation.Key;
                Counts counts = observation.Value;
                if (!_tracked.TryGetValue(key, out TrackedPatch? state))
                {
                    if (_tracked.Count >= MaximumTrackedTuples)
                    {
                        _trackingCapacityReached = true;
                        continue;
                    }
                    state = new TrackedPatch { Present = counts.Before != 0 };
                    _tracked.Add(key, state);
                }

                bool present = counts.After != 0;
                if (installationCompleted && counts.After > 1 && !state.DuplicateWarned)
                {
                    state.DuplicateWarned = true;
                    Enqueue("Bootstrap performance warning: " + Describe(key) + " has " + counts.After +
                        " active registrations. HarmonyX appends repeated registrations; review repeated Patch/PatchAll calls.");
                }

                if (!present && state.Present)
                    state.Removed = true;
                if (installationCompleted && present && !state.Present && counts.Before == 0 &&
                    (state.InstallationCount == 0 || state.Removed))
                {
                    RecordInstallation(key, state, now);
                    state.Removed = false;
                }
                state.Present = present;
            }
        }
    }

    internal bool TryDequeueWarning(out string warning)
    {
        lock (_gate)
        {
            if (_warnings.Count != 0)
            {
                warning = _warnings.Dequeue();
                return true;
            }
            bool reportCapacity = _trackingCapacityReached && !_trackingCapacityReported;
            if (_suppressedWarnings != 0 || reportCapacity)
            {
                warning = "Bootstrap performance diagnostics: " + _suppressedWarnings +
                    " additional warnings suppressed by the bounded queue." +
                    (reportCapacity ? " Tracking capacity reached; further patch tuples are omitted." : "");
                _suppressedWarnings = 0;
                _trackingCapacityReported |= reportCapacity;
                return true;
            }
            warning = "";
            return false;
        }
    }

    private void Count(MethodBase target,
        (MethodInfo Method, HarmonyPatchType Kind, string Owner, int Index)[] source,
        Dictionary<PatchKey, Counts> observations, bool beforeOperation)
    {
        foreach (var patch in source)
        {
            if (patch.Method == null || patch.Owner == null || patch.Owner.Length > MaximumOwnerLength)
            {
                _trackingCapacityReached = true;
                continue;
            }
            var key = new PatchKey(target, patch.Method, patch.Kind, patch.Owner);
            if (!observations.TryGetValue(key, out Counts? counts))
            {
                if (observations.Count >= MaximumTrackedTuples)
                {
                    _trackingCapacityReached = true;
                    continue;
                }
                counts = new Counts();
                observations.Add(key, counts);
            }
            if (beforeOperation) counts.Before++;
            else counts.After++;
        }
    }

    private void RecordInstallation(PatchKey key, TrackedPatch state, long now)
    {
        // A fake or discontinuous clock must not manufacture a rolling-window warning.
        if (state.InstallationCount != 0 && now < state.Installations[state.InstallationCount - 1])
        {
            state.InstallationCount = 0;
            state.LastChurnWarning = null;
        }
        if (state.InstallationCount == 3)
        {
            state.Installations[0] = state.Installations[1];
            state.Installations[1] = state.Installations[2];
            state.InstallationCount = 2;
        }
        state.Installations[state.InstallationCount++] = now;
        if (state.InstallationCount == 3 && now - state.Installations[0] <= ChurnWindowMilliseconds &&
            (!state.LastChurnWarning.HasValue || now - state.LastChurnWarning.Value >= ChurnWindowMilliseconds))
        {
            state.LastChurnWarning = now;
            Enqueue("Bootstrap performance warning: " + Describe(key) +
                " was installed 3 times in 5 minutes after observed removals. Rebuilding patches can retain generated assemblies and detours " +
                "and increase verification cost. Lifecycle reinstalls may be intentional; review process-lifetime patch ownership.");
        }
    }

    private void Enqueue(string warning)
    {
        if (_warnings.Count < MaximumQueuedWarnings)
            _warnings.Enqueue(warning);
        else if (_suppressedWarnings != long.MaxValue)
            _suppressedWarnings++;
    }

    private static string Describe(PatchKey key)
    {
        string assembly;
        try { assembly = key.Method.Module.Assembly.GetName().Name ?? "unknown"; }
        catch { assembly = "unknown"; }
        return "owner '" + SafeText(key.Owner, 160) + "', " + key.Kind + " patch '" + MethodName(key.Method) +
            "' from assembly '" + SafeText(assembly, 160) + "' on '" + MethodName(key.Target) + "'";
    }

    private static string MethodName(MethodBase method)
    {
        try { return SafeText((method.DeclaringType?.FullName ?? "<dynamic>") + "." + method.Name, 256); }
        catch { return "unknown"; }
    }

    private static string SafeText(string value, int maximumLength)
    {
        int length = Math.Min(value.Length, maximumLength);
        var characters = new char[length];
        for (int i = 0; i < length; i++)
            characters[i] = char.IsControl(value[i]) ? ' ' : value[i];
        return new string(characters) + (value.Length > length ? "..." : "");
    }

    private sealed class Counts { internal int Before, After; }
    private sealed class TrackedPatch
    {
        internal bool Present, Removed, DuplicateWarned;
        internal readonly long[] Installations = new long[3];
        internal int InstallationCount;
        internal long? LastChurnWarning;
    }

    private readonly struct PatchKey : IEquatable<PatchKey>
    {
        internal PatchKey(MethodBase target, MethodInfo method, HarmonyPatchType kind, string owner)
        { Target = target; Method = method; Kind = kind; Owner = owner; }
        internal MethodBase Target { get; }
        internal MethodInfo Method { get; }
        internal HarmonyPatchType Kind { get; }
        internal string Owner { get; }
        public bool Equals(PatchKey other) => Target == other.Target && Method == other.Method && Kind == other.Kind &&
            string.Equals(Owner, other.Owner, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is PatchKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Target, Method, Kind, StringComparer.Ordinal.GetHashCode(Owner));
    }
}
