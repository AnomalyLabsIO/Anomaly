using System.Reflection;
using System.Runtime.CompilerServices;
using Anomaly.Bootstrap;
using HarmonyLib;
using HarmonyLib.Public.Patching;

internal static class VerificationSchedulingProbe
{
    internal static void Run(Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        MethodInfo target = typeof(VerificationSchedulingProbe).GetMethod(nameof(RequiredTarget), flags)!;
        MethodInfo prefix = typeof(VerificationSchedulingProbe).GetMethod(nameof(RequiredPrefix), flags)!;
        MethodInfo pending = typeof(VerificationSchedulingProbe).GetMethod(nameof(PendingTarget), flags)!;
        var owner = new HarmonyLib.Harmony("anomaly.guard.probe.client-required");
        long admissionGeneration = RuntimeGuard.Snapshot.Generation;
        owner.Patch(target, prefix: new HarmonyMethod(prefix));
        check(RequiredTarget() == 91, "ordinary required client fixture can install without guard authority");
        RuntimeGuard.RegisterRequiredClientPatch(target, prefix, HarmonyPatchType.Prefix, owner.Id);
        // Compile the exact duplicate-equality path before checking revision reuse;
        // Bootstrap's own first-JIT native entry notifications are relevant work.
        RuntimeGuard.RegisterRequiredClientPatch(target, prefix, HarmonyPatchType.Prefix, owner.Id);

        // Warm the worker runtime before taking the completed checkpoint. No
        // verification runs here, and the worker never owns catalog work.
        int mainThread = Environment.CurrentManagedThreadId;
        check(OnWorker(() => Environment.CurrentManagedThreadId != mainThread), "checkpoint worker is separate from the support thread");
        check(RuntimeGuard.Verify(requireComplete: false), "main checkpoint verifies registered client tuple");
        WarmSharedCheckpoint();
        long completed = RuntimeGuard.CompletedChecks;
        long reused = RuntimeGuard.CheckpointReuses;
        long assemblies = ProtectedMethodCatalog.AssemblyEnumerationCount;
        long resolverWork = ProtectedMethodCatalog.ResolverWorkCount;
        RuntimeGuard.RegisterRequiredClientPatch(target, prefix, HarmonyPatchType.Prefix, owner.Id);
        check(RuntimeGuard.Verify(requireComplete: false), "unchanged main checkpoint reuses completed evidence");
        check(RuntimeGuard.CompletedChecks == completed && RuntimeGuard.CheckpointReuses > reused,
            "duplicate client registration keeps warm checkpoint reusable without another completed check");
        check(OnWorker(() => RuntimeGuard.Verify(requireComplete: false)), "worker consumes completed checkpoint");
        check(ProtectedMethodCatalog.AssemblyEnumerationCount == assemblies &&
            ProtectedMethodCatalog.ResolverWorkCount == resolverWork,
            "unchanged checkpoints do not enumerate assemblies or rerun catalog resolution");
        check(!OnWorker(() => RuntimeGuard.Verify(requireComplete: true)),
            "worker cannot reuse incomplete probe evidence as complete coverage");
        check(RuntimeGuard.Snapshot.Healthy && RuntimeGuard.Snapshot.Generation == admissionGeneration,
            "checkpoint completeness rejection leaves guard health and admission evidence unchanged");
        check(RuntimeGuard.Snapshot.AllowExternalModifications, "scheduling mode exercises permissive coverage assessment");
        check(RuntimeGuard.Verify(requireComplete: true), "main checkpoint assesses missing coverage and publishes its risk");
        check((RuntimeGuard.Snapshot.Flags & 4) != 0 && RuntimeGuard.Snapshot.Healthy,
            "permissive assessment retains incomplete coverage evidence without hiding it");
        Console.WriteLine("SCHEDULING_ASSESSMENT beforeWarm " + DescribeCheckpoint());
        WarmSharedCheckpoint(requireComplete: true);
        Console.WriteLine("SCHEDULING_ASSESSMENT settled " + DescribeCheckpoint());
        check(OnWorker(() => RuntimeGuard.Verify(requireComplete: true)),
            "worker consumes completed permissive coverage assessment");

        ProtectedMethodCatalog.RegisterInfrastructureTarget(pending);
        check(!OnWorker(() => RuntimeGuard.Verify(requireComplete: false)),
            "worker rejects a checkpoint invalidated by pending protected registration");
        check(RuntimeGuard.Snapshot.Healthy, "pending work rejection does not poison guard health");
        check(RuntimeGuard.Verify(requireComplete: false), "main checkpoint completes newly registered target work");
        check(OnWorker(() => RuntimeGuard.Verify(requireComplete: false)), "worker can reuse checkpoint after main completion");

        PatchDiagnosticsProbe.Run(check);
        CheckScheduledSlices(target, owner.Id, prefix, check);

        // Bypass the supported removal hooks just like direct PatchInfo mutation.
        // The required tuple invariant must notice the changed authoritative array.
        var info = PatchManager.GetPatchInfo(target);
        info.prefixes = info.prefixes.Where(patch => patch.owner != owner.Id || patch.PatchMethod != prefix).ToArray();
        check(!RuntimeGuard.Verify(requireComplete: false) && !RuntimeGuard.Snapshot.Healthy,
            "raw registered-client patch removal invalidates completed checkpoint and fails closed");
    }

    private static void WarmSharedCheckpoint(bool requireComplete = false)
    {
        // Reading a protected Bootstrap metric or its cached-check helper for the
        // first time can itself produce a native entry notification. Establish
        // consecutive exact reuses before treating the process as settled.
        _ = RuntimeGuard.CompletedChecks;
        _ = RuntimeGuard.CheckpointReuses;
        _ = RuntimeGuard.AuditedPatchRecords;
        _ = RuntimeGuard.LastSliceItems;
        _ = RuntimeGuard.LastSliceOverruns;
        _ = ProtectedMethodCatalog.AssemblyEnumerationCount;
        _ = ProtectedMethodCatalog.ResolverWorkCount;
        _ = ProtectedMethodCatalog.NativeRevision;
        _ = ProtectedMethodCatalog.HasPendingWork;
        var snapshot = RuntimeGuard.Snapshot;
        _ = snapshot.Healthy;
        _ = snapshot.Flags;
        _ = snapshot.Generation;
        _ = snapshot.AllowExternalModifications;
        _ = snapshot.MutationInProgress;
        _ = DescribeCheckpoint();
        int consecutive = 0;
        for (int attempt = 0; attempt < 64; ++attempt)
        {
            long completed = RuntimeGuard.CompletedChecks;
            long reused = RuntimeGuard.CheckpointReuses;
            if (!RuntimeGuard.Verify(requireComplete))
                throw new InvalidOperationException("Shared checkpoint failed while warming cached runtime paths.");
            bool cached = RuntimeGuard.CompletedChecks == completed && RuntimeGuard.CheckpointReuses > reused;
            consecutive = cached ? consecutive + 1 : 0;
            if (consecutive == 3) return;
        }
        throw new InvalidOperationException("Shared checkpoint did not settle after 64 attempts: completed=" +
            RuntimeGuard.CompletedChecks + ", reused=" + RuntimeGuard.CheckpointReuses +
            ", catalogNative=" + ProtectedMethodCatalog.NativeRevision +
            ", catalogPending=" + ProtectedMethodCatalog.HasPendingWork +
            ", assemblies=" + ProtectedMethodCatalog.AssemblyEnumerationCount +
            ", resolver=" + ProtectedMethodCatalog.ResolverWorkCount + ", " + DescribeCheckpoint());
    }

    private static string DescribeCheckpoint()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        Type guard = typeof(RuntimeGuard);
        object? completion = guard.GetField("_completion", flags)!.GetValue(null);
        object current = guard.GetMethod("CurrentStamp", flags)!.Invoke(null, null)!;
        static object? Backing(object value, string name) => value.GetType().GetField("<" + name + ">k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value);
        static string Stamp(object value) => string.Join(",", value.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(field => field.FieldType == typeof(long)).Select(field => field.Name + "=" + field.GetValue(value)));
        string completed = completion == null ? "none" : Stamp(Backing(completion, "Stamp")!);
        string eligible = completion == null ? "none" : Backing(completion, "Complete")!.ToString()!;
        long age = completion == null ? -1 : Environment.TickCount64 - (long)Backing(completion, "Tick")!;
        return "current=[" + Stamp(current) + "] completed=[" + completed + "] eligible=" + eligible +
            " ageMs=" + age + " working=" + guard.GetField("_working", flags)!.GetValue(null) +
            " pending=" + guard.GetProperty("HasPendingWork", flags)!.GetValue(null) +
            " native=" + ProtectedMethodCatalog.NativeRevision;
    }

    private static void CheckScheduledSlices(MethodBase target, string owner, MethodInfo prefix, Action<bool, string> check)
    {
        // Compile the frame coordinator and slice metrics before comparing two
        // otherwise identical scheduled cohorts. Its first native entries are
        // legitimate catalog work, rather than patch-record work in the fixture.
        RuntimeGuard.Update();
        WarmSharedCheckpoint();
        check(RuntimeGuard.Verify(requireComplete: false), "main checkpoint settles diagnostic fixture work before slice checks");
        long assemblies = ProtectedMethodCatalog.AssemblyEnumerationCount;
        long resolverWork = ProtectedMethodCatalog.ResolverWorkCount;
        var baseline = RunScheduledPass(check);
        var info = PatchManager.GetPatchInfo(target);
        Patch[] original = info.prefixes;
        Patch record = original.Single(patch => patch.owner == owner && patch.PatchMethod == prefix);
        // Probe-only metadata cohort. It adds no native wrappers or live patch
        // installations and is restored before the final raw-removal test.
        const int longChainLength = 96;
        try
        {
            info.prefixes = Enumerable.Repeat(record, longChainLength).ToArray();
            var longChain = RunScheduledPass(check);
            check(longChain.Frames > 1, "long mandatory metadata cohort completes across multiple frames");
            check(longChain.PatchRecords >= longChainLength,
                "long mandatory cohort audits individual patch records within the per-frame item allowance");
            Console.WriteLine("SCHEDULING_COHORT baselineFrames=" + baseline.Frames + " baselineItems=" + baseline.Items +
                " baselineRecords=" + baseline.PatchRecords + " longFrames=" + longChain.Frames +
                " longItems=" + longChain.Items + " longRecords=" + longChain.PatchRecords);
            check(ProtectedMethodCatalog.AssemblyEnumerationCount == assemblies &&
                ProtectedMethodCatalog.ResolverWorkCount == resolverWork,
                "scheduled invariant passes do not enumerate assemblies or rerun settled catalog resolution");
        }
        finally { info.prefixes = original; }
        check(RuntimeGuard.Verify(requireComplete: false), "restored metadata passes the explicit checkpoint");
    }

    private static (int Frames, int Items, long PatchRecords) RunScheduledPass(Action<bool, string> check)
    {
        // A test clock reset removes wall-clock sleeps without affecting the
        // production coordinator or its revision/coverage evidence.
        typeof(RuntimeGuard).GetField("_nextChecks", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0L);
        long completed = RuntimeGuard.CompletedChecks;
        long records = RuntimeGuard.AuditedPatchRecords;
        long native = ProtectedMethodCatalog.NativeRevision;
        int frames = 0;
        int items = 0;
        while (frames < 512 && RuntimeGuard.CompletedChecks == completed && RuntimeGuard.Snapshot.Healthy)
        {
            long beforeRecords = RuntimeGuard.AuditedPatchRecords;
            RuntimeGuard.Update();
            int slice = RuntimeGuard.LastSliceItems;
            long recordDelta = RuntimeGuard.AuditedPatchRecords - beforeRecords;
            if (slice < 0 || slice > GuardWorkBudget.MaximumItems)
                throw new InvalidOperationException("Scheduled frame exceeded its 32-item budget: " + slice);
            if (recordDelta < 0 || recordDelta > slice)
                throw new InvalidOperationException("Scheduled frame audited " + recordDelta +
                    " patch records but charged only " + slice + " work items at frame " + frames + ".");
            items += slice;
            ++frames;
        }
        check(RuntimeGuard.CompletedChecks > completed && RuntimeGuard.Snapshot.Healthy,
            "bounded scheduled frames eventually publish completed invariant checks");
        long totalRecords = RuntimeGuard.AuditedPatchRecords - records;
        Console.WriteLine("SCHEDULING_PASS frames=" + frames + " items=" + items + " patchRecords=" + totalRecords +
            " completedDelta=" + (RuntimeGuard.CompletedChecks - completed) + " nativeBefore=" + native +
            " nativeAfter=" + ProtectedMethodCatalog.NativeRevision);
        return (frames, items, totalRecords);
    }

    private static bool OnWorker(Func<bool> action)
    {
        bool result = false;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        worker.Start();
        if (!worker.Join(5000))
            throw new TimeoutException("Worker checkpoint did not finish within five seconds.");
        if (failure != null)
            throw new InvalidOperationException("Worker checkpoint failed.", failure);
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)] private static int RequiredTarget() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int PendingTarget() => 1;
    private static bool RequiredPrefix(ref int __result) { __result = 91; return false; }
}
