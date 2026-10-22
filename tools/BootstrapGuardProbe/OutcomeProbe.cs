using System.Reflection;
using System.Runtime.CompilerServices;
using Anomaly.Bootstrap;
using HarmonyLib;
using MonoMod.RuntimeDetour;

internal static class OutcomeProbe
{
    internal static void Run(string mode, Action<bool, string> check)
    {
        MethodInfo target = typeof(OutcomeProbe).GetMethod(nameof(Target), BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo prefix = typeof(OutcomeProbe).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)!;
        ProtectedMethodCatalog.RegisterExternalTarget(target);
        var harmony = new HarmonyLib.Harmony("anomaly.guard.probe.outcome");
        long before = RuntimeGuard.Snapshot.Generation;
        harmony.Unpatch(target, prefix);
        harmony.Unpatch(target, HarmonyPatchType.All, "owner-that-has-no-patch");
        check(RuntimeGuard.Snapshot.Generation == before, "unpatch selectors with no matching patches do not advance risk");
        if (mode != "allow") return;
        using var hook = new Hook(target, (Func<int>)(() => 222));
        before = RuntimeGuard.Snapshot.Generation;
        hook.Apply();
        check(RuntimeGuard.Snapshot.Generation == before, "already-applied detour Apply is not another modification");
        Func<Hook, bool> veto = candidate => !ReferenceEquals(candidate, hook);
        Hook.OnUndo += veto;
        try { hook.Undo(); }
        finally { Hook.OnUndo -= veto; }
        check(hook.IsApplied && Target() == 222, "foreign undo veto retains the existing detour");
        check(RuntimeGuard.Snapshot.Generation == before, "vetoed detour undo does not advance risk");
        hook.Undo();
        check(!hook.IsApplied && RuntimeGuard.Snapshot.Generation > before, "actual detour removal advances risk");
    }

    [MethodImpl(MethodImplOptions.NoInlining)] private static int Target() => 1;
    private static bool Prefix(ref int __result) { __result = 99; return false; }
}
