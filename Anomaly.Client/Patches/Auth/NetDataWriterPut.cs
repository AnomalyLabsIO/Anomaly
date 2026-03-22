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
        MelonLogger.Msg("GetConnectData intercepted - writing auth data");
        AuthHandler.AuthWrite(writer);
        return false;
    }

    [HarmonyPatch(typeof(CustomLiteNetLib4MirrorTransport), nameof(CustomLiteNetLib4MirrorTransport.OnConncetionRefused))]
    [HarmonyPrefix]
    public static bool ConnectionRefused(DisconnectInfo disconnectinfo)
    {
        MelonLogger.Msg($"Connection refused - Reason: {disconnectinfo.Reason}, HasData: {disconnectinfo.AdditionalData?.RawData != null}");
        return AuthHandler.RejectAuth(disconnectinfo);
    }

    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientDisconnect))]
    [HarmonyPrefix]
    public static void OnClientDisconnect()
    {
        AuthHandler.OnDisconnected();
    }
}
