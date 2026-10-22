using System.Reflection;
using Anomaly.Client.Internal.Poses;
using HarmonyLib;
using Il2Cpp;
using Il2CppMirror;
using Il2CppPlayerRoles;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson;
using MelonLoader;

namespace Anomaly.Client.Patches.Poses;

internal static class PoseReceiptPatches
{
    internal static void VerifyInstalled()
    {
        string owner = Core.Instance?.HarmonyInstance?.Id;
        foreach (var method in NativeReceipt.TargetMethods()) Require(method, owner, typeof(NativeReceipt));
        Require(AccessTools.Method(typeof(PlayerRoleManager), nameof(PlayerRoleManager.InitializeNewRole)), owner, typeof(RoleInitialization));
        Require(AccessTools.Method(typeof(CharacterModel), nameof(CharacterModel.ResetObject)), owner, typeof(ModelReset));
        Require(AccessTools.Method(typeof(ReferenceHub), nameof(ReferenceHub.OnDestroy)), owner, typeof(PlayerDestroyed));
    }

    private static void Require(MethodBase method, string owner, Type patch)
    {
        var info = method == null ? null : HarmonyLib.Harmony.GetPatchInfo(method);
        if (owner == null || info == null || !info.Prefixes.Any(p => p.owner == owner && p.PatchMethod.DeclaringType == patch))
            throw new InvalidOperationException($"Required pose lifecycle patch is absent: {method?.Name}.");
    }

    [HarmonyPatch]
    private static class NativeReceipt
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PlayerRolesNetUtils), nameof(PlayerRolesNetUtils.ReadRoleSyncInfo), new[] { typeof(NetworkReader) });
            yield return AccessTools.Constructor(typeof(RoleSyncInfo), new[] { typeof(NetworkReader) });
        }

        [HarmonyPrefix]
        private static void Prefix(NetworkReader reader, out PoseReceiptLedger.ReadScope __state)
        {
            __state = null;
            try { __state = PoseReceiptLedger.BeginRead(reader); }
            catch (Exception ex) { MelonLogger.Warning($"Pose native receipt capture failed: {ex.Message}"); }
        }

        [HarmonyPostfix]
        private static void Postfix(bool __runOriginal, PoseReceiptLedger.ReadScope __state) => PoseReceiptLedger.EndRead(__state, __runOriginal);

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, PoseReceiptLedger.ReadScope __state)
        {
            if (__exception != null) PoseReceiptLedger.EndRead(__state, false);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PlayerRoleManager), nameof(PlayerRoleManager.InitializeNewRole))]
    private static class RoleInitialization
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerRoleManager __instance, RoleTypeId targetId, NetworkReader data, out PoseReceiptLedger.Receipt __state)
        {
            __state = null;
            try { __state = PoseReceiptLedger.BeforeInitialize(__instance._hub, targetId, data); }
            catch (Exception ex) { MelonLogger.Warning($"Pose role lifecycle capture failed: {ex.Message}"); }
        }

        [HarmonyPostfix]
        private static void Postfix(PlayerRoleManager __instance, bool __runOriginal, PoseReceiptLedger.Receipt __state)
        {
            try
            {
                if (__runOriginal) PoseReceiptLedger.AfterInitialize(__instance._hub, __state);
                else if (__state != null) { __state.Failed = true; PoseReceiptLedger.Invalidate(__state.NetId); }
            }
            catch (Exception ex) { MelonLogger.Warning($"Pose initialization binding failed: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.ResetObject))]
    private static class ModelReset
    {
        [HarmonyPrefix]
        private static void Prefix(CharacterModel __instance)
        {
            try { PoseRuntime.ReleaseModel(__instance); }
            catch (Exception ex) { MelonLogger.Warning($"Pose pooled model release failed: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(ReferenceHub), nameof(ReferenceHub.OnDestroy))]
    private static class PlayerDestroyed
    {
        [HarmonyPrefix]
        private static void Prefix(ReferenceHub __instance)
        {
            try { PoseReceiptLedger.Forget(__instance.netId); }
            catch (Exception ex) { MelonLogger.Warning($"Pose player release failed: {ex.Message}"); }
        }
    }
}
