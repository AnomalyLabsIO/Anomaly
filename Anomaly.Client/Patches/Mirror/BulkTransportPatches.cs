using System;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Internal.Networking;
using Anomaly.Shared.Networking;
using HarmonyLib;
using Il2CppMirror;
using Il2CppMirror.LiteNetLib4Mirror;

namespace Anomaly.Client.Patches.Mirror;

[HarmonyPatch(typeof(LiteNetLib4MirrorCore), nameof(LiteNetLib4MirrorCore.SetOptions))]
internal static class BulkTransportOptionsPatch
{
    [HarmonyPrefix]
    internal static void Prefix()
    {
        try { BulkTransportRuntime.Prepare(); }
        catch (Exception error)
        {
            BulkTransportRuntime.Unavailable(error);
            AnomalyMessaging.FailClosedTransport(
                "Bulk transport could not be prepared before connection. Reconnect or restart the client.");
            throw;
        }
    }

    [HarmonyPostfix]
    internal static void Postfix()
    {
        try { BulkTransportRuntime.CompletePreparation(); }
        catch (Exception error)
        {
            BulkTransportRuntime.Unavailable(error);
            AnomalyMessaging.FailClosedTransport(
                "Bulk transport capacity changed during connection setup. Reconnect or restart the client.");
            throw;
        }
    }
}

[HarmonyPatch(typeof(Transport), nameof(Transport.GetBatchThreshold))]
internal static class BulkBatchThresholdPatch
{
    [HarmonyPrefix]
    internal static bool Prefix(Transport __instance, int channelId, ref int __result)
    {
        if (!BulkTransportRuntime.IsOwnedTransport(__instance) || !BulkTransportRuntime.IsOwnedChannel(channelId)) return true;
        __result = BulkTransferProtocol.BatchThresholdBytes;
        return false;
    }
}
