using System.Reflection;
using System.Runtime.CompilerServices;
using Anomaly.Bootstrap;
using HarmonyLib;

internal static class AllowedRebuildProbe
{
    internal static void Run(string mode, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        MethodInfo target = typeof(AllowedRebuildProbe).GetMethod(nameof(External), flags)!;
        MethodInfo required = typeof(AllowedRebuildProbe).GetMethod(nameof(RequiredPrefix), flags)!;
        MethodInfo foreign = typeof(AllowedRebuildProbe).GetMethod(nameof(ForeignPrefix), flags)!;
        ProtectedMethodCatalog.RegisterExternalTarget(target);
        ProtectedMethodCatalog.RegisterTrustedPatch(target, required, HarmonyPatchType.Prefix);
        var requiredOwner = new HarmonyLib.Harmony("anomaly.guard.probe.required-rebuild");
        var foreignOwner = new HarmonyLib.Harmony("anomaly.guard.probe.foreign-rebuild");
        requiredOwner.Patch(target, prefix: new HarmonyMethod(required));
        check(External() == 1 && HasRequired(target, required), "required external prefix is installed before rebuild probe");

        long before = RuntimeGuard.Snapshot.Generation;
        foreignOwner.Patch(target, prefix: new HarmonyMethod(foreign));
        check(External() == (mode == "allow" ? 99 : 1), "foreign external patch rebuilds an existing required backend only when allowed");
        check(HasRequired(target, required), "required prefix survives external patch addition");
        if (mode == "allow")
        {
            check(RuntimeGuard.Snapshot.Generation > before && (RuntimeGuard.Snapshot.Flags & 1) != 0,
                "allowed external backend rebuild records Harmony risk");
            long applied = RuntimeGuard.Snapshot.Generation;
            foreignOwner.Unpatch(target, foreign);
            check(External() == 1 && HasRequired(target, required), "foreign external unpatch restores behavior and preserves required prefix");
            check(RuntimeGuard.Snapshot.Generation > applied, "allowed external unpatch advances the audit generation");
        }
        else
        {
            check(RuntimeGuard.Snapshot.Generation == before, "blocked external backend rebuild does not record applied risk");
            check(HarmonyLib.Harmony.GetPatchInfo(target)!.Prefixes.All(patch => patch.PatchMethod != foreign),
                "blocked external backend rebuild leaves no latent foreign prefix");
        }
    }

    private static bool HasRequired(MethodBase target, MethodInfo required) =>
        HarmonyLib.Harmony.GetPatchInfo(target)?.Prefixes.Any(patch => patch.PatchMethod == required) == true;

    [MethodImpl(MethodImplOptions.NoInlining)] private static int External() => 1;
    private static void RequiredPrefix() { }
    private static bool ForeignPrefix(ref int __result) { __result = 99; return false; }
}
