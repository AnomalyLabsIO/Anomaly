using Anomaly.Client.Auth;
using HarmonyLib;
using Il2Cpp;
using Il2CppLiteNetLib;
using Il2CppLiteNetLib.Utils;
using Il2CppMirror;
using Il2CppMirror.LiteNetLib4Mirror;
using MelonLoader;

namespace Anomaly.Client.Patches.Auth;

/// <summary>
/// Authentication adapters. Writer transactions and connection state own their
/// lifetime; unrelated callbacks in the same Unity frame are not interchangeable.
/// </summary>
[HarmonyPatch]
public static class NetDataWriterPut
{
    [HarmonyPatch(typeof(CustomLiteNetLib4MirrorTransport), nameof(CustomLiteNetLib4MirrorTransport.GetConnectData))]
    [HarmonyPrefix]
    public static bool OnPrefix1(NetDataWriter writer)
    {
        if (NetworkServer.active)
        {
            AuthHandler.EnterLocalHostScope();
            MelonLogger.Msg("GetConnectData intercepted - local hosted server, skipping Anomaly auth");
            return true;
        }

        MelonLogger.Msg("GetConnectData intercepted - writing auth data");
        AuthHandler.AuthWrite(writer);
        return false;
    }

    [HarmonyPatch(typeof(CustomLiteNetLib4MirrorTransport), nameof(CustomLiteNetLib4MirrorTransport.OnConncetionRefused))]
    [HarmonyPrefix]
    public static bool ConnectionRefused(
        CustomLiteNetLib4MirrorTransport __instance,
        DisconnectInfo disconnectinfo)
    {
        if (!AuthHandler.UsingAnomalyAuth)
            return true;

        try
        {
            MelonLogger.Msg(
                $"Connection refused - Reason: {disconnectinfo.Reason}, " +
                $"HasData: {disconnectinfo.AdditionalData?.RawData != null}");
        }
        catch { }
        return AuthHandler.RejectAuth(disconnectinfo);
    }

    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientDisconnect))]
    [HarmonyPrefix]
    public static void OnClientDisconnect(CustomNetworkManager __instance)
    {
        AuthHandler.OnDisconnected();
    }

    [HarmonyPatch(typeof(LiteNetLib4MirrorClient), nameof(LiteNetLib4MirrorClient.OnPeerDisconnected))]
    [HarmonyPrefix]
    public static void ObservePeerDisconnect(NetPeer peer, DisconnectInfo disconnectinfo)
    {
        // The transport queues its main-thread disconnect continuation. Capture
        // detached identity and bounded data here, before a pooled reader can
        // be recycled. Never advance the stock reader or create UI here.
        try { AuthHandler.ObserveTransportDisconnect(peer, disconnectinfo); }
        catch (Exception exception)
        {
            try { MelonLogger.Warning("Could not capture transport disconnect: " + exception.Message); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(LiteNetLib4MirrorTransport), nameof(LiteNetLib4MirrorTransport.ClientDisconnect))]
    [HarmonyPrefix]
    public static void ObserveLocalDisconnect() => AuthHandler.ObserveLocalTransportStop();
}
