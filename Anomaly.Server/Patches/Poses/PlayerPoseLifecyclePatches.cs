using System;
using Anomaly.Server.Poses;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using Mirror;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;

namespace Anomaly.Server.Patches.Poses;

internal static class PlayerPoseLifecyclePatches
{
    [HarmonyPatch(typeof(PlayerRoleManager), nameof(PlayerRoleManager.InitializeNewRole))]
    private static class ModelInitialization
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerRoleManager __instance, bool __runOriginal)
        {
            try { if (__runOriginal && NetworkServer.active) PlayerPoseRuntime.ModelInitialized(__instance.Hub); }
            catch (Exception exception) { Utils.OffThreadLogger.Warn("Pose model invalidation failed: " + exception.Message); }
        }
    }

    [HarmonyPatch(typeof(RoleSyncInfo), nameof(RoleSyncInfo.Write))]
    private static class RoleWrite
    {
        [HarmonyPostfix]
        private static void Postfix(NetworkWriter writer, uint ____targetNetId, uint ____receiverNetId,
            RoleTypeId ____targetRole, PlayerRoleBase ____role, bool __runOriginal)
        {
            if (!__runOriginal) return;
            try
            {
                NativePoseDeliveryCapture.RoleWritten(writer, new NativePoseDeliveryCapture.Record {
                    Target = ____targetNetId, Recipient = ____receiverNetId, Role = (sbyte)____targetRole,
                    Life = ____role?.UniqueLifeIdentifier ?? 0 });
            }
            catch (Exception exception) { Utils.OffThreadLogger.Warn("Pose role receipt capture failed: " + exception.Message); }
        }
    }

    [HarmonyPatch(typeof(RoleSyncInfoPack), nameof(RoleSyncInfoPack.WritePlayers))]
    private static class PackWrite
    {
        [HarmonyPrefix] private static void Prefix(NetworkWriter writer) => NativePoseDeliveryCapture.BeginPack(writer);
        [HarmonyPostfix] private static void Postfix(NetworkWriter writer, bool __runOriginal)
        {
            if (__runOriginal) NativePoseDeliveryCapture.EndPack(writer);
            else NativePoseDeliveryCapture.AbortPack();
        }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception != null) NativePoseDeliveryCapture.AbortPack();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NetworkConnection), nameof(NetworkConnection.Send), new[] { typeof(ArraySegment<byte>), typeof(int) })]
    private static class Enqueue
    {
        [HarmonyPrefix]
        private static void Prefix(ArraySegment<byte> segment, int channelId, out NativePoseDeliveryCapture.Pending __state) =>
            __state = NativePoseDeliveryCapture.BeforeEnqueue(segment, channelId);
        [HarmonyPostfix]
        private static void Postfix(NetworkConnection __instance, NativePoseDeliveryCapture.Pending __state, bool __runOriginal)
        {
            try { if (__runOriginal) NativePoseDeliveryCapture.AfterEnqueue(__state, __instance); }
            catch (Exception exception) { Utils.OffThreadLogger.Warn("Pose role delivery binding failed: " + exception.Message); }
        }
    }

    // Observe the final value after every LabAPI subscriber has run.
    [HarmonyPatch(typeof(LabApi.Events.Handlers.PlayerEvents), "OnValidatedVisibility")]
    private static class Visibility
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerValidatedVisibilityEventArgs __0)
        {
            try { PlayerPoseRuntime.ObserveVisibility(__0.Player?.ReferenceHub, __0.Target?.ReferenceHub, __0.IsVisible); }
            catch (Exception exception) { Utils.OffThreadLogger.Warn("Pose visibility observation failed: " + exception.Message); }
        }
    }

    [HarmonyPatch(typeof(FirstPersonMovementModule), nameof(FirstPersonMovementModule.ServerOverridePosition))]
    private static class Teleport
    {
        [HarmonyPostfix]
        private static void Postfix(FirstPersonMovementModule __instance, bool __runOriginal)
        {
            try { if (__runOriginal) PlayerPoseRuntime.RootDiscontinuity(__instance.Hub); }
            catch (Exception exception) { Utils.OffThreadLogger.Warn("Pose teleport reset failed: " + exception.Message); }
        }
    }
}
