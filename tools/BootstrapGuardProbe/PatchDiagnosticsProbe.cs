using System.Reflection;
using System.Runtime.CompilerServices;
using Anomaly.Bootstrap;
using HarmonyLib;

internal static class PatchDiagnosticsProbe
{
    internal static void Run(Action<bool, string> check)
    {
        DrainWarnings();
        var snapshot = RuntimeGuard.Snapshot;
        var direct = new HarmonyLib.Harmony("anomaly.guard.probe.diagnostic-direct");
        MethodInfo prefix = Method(nameof(Prefix));
        MethodInfo directTarget = Method(nameof(DirectTarget));
        check(PatchPerformanceDiagnostics.GetProcessorOwner(new PatchProcessor(direct, directTarget)) == direct.Id,
            "direct processor adapter reads its exact Harmony owner");
        check(PatchPerformanceDiagnostics.GetProcessorOwner(new object()) == null &&
            !PatchPerformanceDiagnostics.IsClassInstallationCompleted(new object()),
            "unsupported diagnostic objects do not manufacture owner or success evidence");
        direct.Patch(directTarget, prefix: new HarmonyMethod(prefix));
        check(DrainWarnings().Count == 0, "first direct installation produces no advisory warning");
        direct.Patch(directTarget, prefix: new HarmonyMethod(prefix));
        check(HasDuplicate(directTarget, prefix, direct.Id), "direct duplicate exists in authoritative Harmony metadata");
        check(IsOneWarning(DrainWarnings(), direct.Id, "active registrations"), "direct installed-hook duplicate emits one attributed warning");
        direct.Patch(directTarget, prefix: new HarmonyMethod(prefix));
        check(DrainWarnings().Count == 0, "direct duplicate warning is emitted once per tuple per process");

        var classOwner = new HarmonyLib.Harmony("anomaly.guard.probe.diagnostic-class");
        var classProcessor = classOwner.CreateClassProcessor(typeof(ClassPatch));
        check(PatchPerformanceDiagnostics.GetProcessorOwner(classProcessor) == classOwner.Id,
            "class processor adapter reads its exact Harmony owner");
        classProcessor.Patch();
        check(DrainWarnings().Count == 0, "first class installation produces no advisory warning");
        classOwner.CreateClassProcessor(typeof(ClassPatch)).Patch();
        MethodInfo classPrefix = typeof(ClassPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
        check(HasDuplicate(Method(nameof(ClassTarget)), classPrefix, classOwner.Id), "class duplicate exists in authoritative Harmony metadata");
        check(IsOneWarning(DrainWarnings(), classOwner.Id, "active registrations"), "class installed-hook duplicate emits one attributed warning");
        classOwner.CreateClassProcessor(typeof(ClassPatch)).Patch();
        check(DrainWarnings().Count == 0, "class duplicate warning is emitted once per tuple per process");

        var churn = new HarmonyLib.Harmony("anomaly.guard.probe.diagnostic-churn");
        MethodInfo churnTarget = Method(nameof(ChurnTarget));
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            churn.Patch(churnTarget, prefix: new HarmonyMethod(prefix));
            if (cycle < 2)
            {
                check(DrainWarnings().Count == 0, "churn warning waits for the third successful installation");
                churn.Unpatch(churnTarget, prefix);
                check(DrainWarnings().Count == 0, "ordinary removal produces no advisory warning");
            }
        }
        check(IsOneWarning(DrainWarnings(), churn.Id, "installed 3 times in 5 minutes"),
            "installed hooks identify three successful installations with removals");

        var replacement = new HarmonyLib.Harmony("anomaly.guard.probe.diagnostic-replacement");
        MethodInfo replacementTarget = Method(nameof(ReplacementTarget));
        replacement.Patch(replacementTarget, prefix: new HarmonyMethod(prefix));
        replacement.Unpatch(replacementTarget, prefix);
        replacement.Patch(replacementTarget, prefix: new HarmonyMethod(prefix));
        check(DrainWarnings().Count == 0, "two-install replacement remains below duplicate and churn warning thresholds");
        var after = RuntimeGuard.Snapshot;
        check(after.Healthy && after.Flags == snapshot.Flags && after.Generation == snapshot.Generation,
            "ordinary diagnostic fixture operations leave guard flags and admission generation unchanged");

        var blocked = new HarmonyLib.Harmony("anomaly.guard.probe.diagnostic-blocked");
        MethodInfo protectedTarget = typeof(Fixture).GetMethod(nameof(Fixture.Protected))!;
        blocked.Patch(protectedTarget, prefix: new HarmonyMethod(prefix));
        check(HarmonyLib.Harmony.GetPatchInfo(protectedTarget)?.Prefixes.Any(patch => patch.owner == blocked.Id) != true,
            "blocked diagnostic fixture installation leaves no patch registration");
        check(DrainWarnings().Count == 0, "blocked installation produces no advisory success warning");
    }

    private static MethodInfo Method(string name) => typeof(PatchDiagnosticsProbe)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static bool HasDuplicate(MethodBase target, MethodInfo prefix, string owner) =>
        HarmonyLib.Harmony.GetPatchInfo(target)?.Prefixes.Count(patch => patch.PatchMethod == prefix && patch.owner == owner) > 1;

    private static bool IsOneWarning(List<string> warnings, string owner, string reason) => warnings.Count == 1 &&
        warnings[0].Contains(owner, StringComparison.Ordinal) && warnings[0].Contains(reason, StringComparison.Ordinal) &&
        warnings[0].Contains(typeof(PatchDiagnosticsProbe).Assembly.GetName().Name!, StringComparison.Ordinal);

    private static List<string> DrainWarnings()
    {
        var result = new List<string>();
        while (PatchPerformanceDiagnostics.TryDequeueWarning(out string warning)) result.Add(warning);
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)] private static int DirectTarget() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int ClassTarget() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int ChurnTarget() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int ReplacementTarget() => 1;
    private static void Prefix() { }

    [HarmonyPatch(typeof(PatchDiagnosticsProbe), nameof(ClassTarget))]
    private static class ClassPatch
    {
        [HarmonyPrefix] private static void Prefix() { }
    }
}
