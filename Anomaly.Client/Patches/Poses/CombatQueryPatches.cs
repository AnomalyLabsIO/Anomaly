using System.Reflection;
using Anomaly.Client.Internal.Poses;
using HarmonyLib;
using Il2CppPlayerRoles.PlayableScps.Scp049;
using Il2CppPlayerRoles.PlayableScps.Scp173;
using MelonLoader;

namespace Anomaly.Client.Patches.Poses;

/// <summary>Only the two native target acquisition raycasts see server-authorized combat bones.</summary>
[HarmonyPatch]
internal static class CombatQueryPatches
{
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Scp049AttackAbility), "CanFindTarget");
        yield return AccessTools.Method(typeof(Scp173SnapAbility), "TryHitTarget");
    }

    internal static void VerifyInstalled()
    {
        string owner = Core.Instance?.HarmonyInstance?.Id;
        foreach (MethodBase method in TargetMethods())
        {
            var info = method == null ? null : HarmonyLib.Harmony.GetPatchInfo(method);
            if (owner == null || info == null ||
                !info.Prefixes.Any(p => p.owner == owner && p.PatchMethod.DeclaringType == typeof(CombatQueryPatches)) ||
                !info.Postfixes.Any(p => p.owner == owner && p.PatchMethod.DeclaringType == typeof(CombatQueryPatches)) ||
                !info.Finalizers.Any(p => p.owner == owner && p.PatchMethod.DeclaringType == typeof(CombatQueryPatches)))
                throw new InvalidOperationException("Required combat acquisition scope patch is absent: " + method?.Name);
        }
    }

    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    private static void Prefix(out IDisposable __state) => __state = CombatPoseRuntime.BeginQuery();

    [HarmonyPostfix, HarmonyPriority(Priority.First)]
    private static void Postfix(IDisposable __state) => Restore(__state);

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, IDisposable __state)
    {
        Restore(__state);
        return __exception;
    }

    private static void Restore(IDisposable scope)
    {
        try { scope?.Dispose(); }
        catch (Exception exception)
        {
            try { MelonLogger.Error("Combat query restoration failed: " + exception); } catch { }
            try { CombatPoseRuntime.Shutdown(); }
            catch (Exception cleanup) { try { MelonLogger.Error("Combat bridge cleanup failed: " + cleanup); } catch { } }
        }
    }
}
