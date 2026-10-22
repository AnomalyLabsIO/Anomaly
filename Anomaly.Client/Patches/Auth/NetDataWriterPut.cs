using Anomaly.Client.Auth;
using HarmonyLib;
using Il2Cpp;
using Il2CppLiteNetLib;
using Il2CppLiteNetLib.Utils;
using Il2CppMirror;
using Il2CppMirror.LiteNetLib4Mirror;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Patches.Auth;

/// <summary>
/// Harmony patches for the client authentication flow.
///
/// In IL2CPP, Harmony patches on virtual method overrides fire twice per call
/// due to vtable slot dispatch (once for the derived class, once for the base).
/// We use frame-count guards to deduplicate: two calls in the same frame from
/// the same virtual dispatch are collapsed into one.
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

        // IL2CPP dispatches virtual-method prefixes twice per invocation (once
        // for the derived slot, once for the base). The first call consumes
        // bytes from DisconnectInfo.AdditionalData inside RejectAuth; a second
        // call in the same frame would read past the consumed bytes, misread
        // the handshake response as a real rejection, and forward it to the
        // vanilla handler — which tears down the connection.
        if (!PatchInvocationGuard.ShouldFireAuthentication(
                __instance,
                PatchInvocationGuard.ConnectionRefused))
            return false;

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
        if (!PatchInvocationGuard.ShouldFireAuthentication(
                __instance,
                PatchInvocationGuard.ClientDisconnect))
            return;

        AuthHandler.OnDisconnected();
    }

    [HarmonyPatch(typeof(LiteNetLib4MirrorClient), nameof(LiteNetLib4MirrorClient.OnPeerDisconnected))]
    [HarmonyPrefix]
    public static void ObservePeerDisconnect(NetPeer peer, DisconnectInfo disconnectinfo)
    {
        // The transport queues its main-thread disconnect continuation. Capture
        // only detached reason/identity here; never create UI in this callback.
        try { AuthHandler.ObserveTransportDisconnect(peer, disconnectinfo.Reason); }
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
