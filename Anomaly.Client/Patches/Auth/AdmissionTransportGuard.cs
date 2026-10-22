using System.Reflection;
using Anomaly.Client.Auth;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppLiteNetLib;
using Il2CppLiteNetLib.Utils;
using Il2CppMirror.LiteNetLib4Mirror;
using Il2CppSystem.Net;

namespace Anomaly.Client.Patches.Auth;

[HarmonyPatch]
internal static class AdmissionTransportGuard
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return Require("SendRawAndRecycle", typeof(NetPacket), typeof(IPEndPoint));
        yield return Require("SendRaw", typeof(NetPacket), typeof(IPEndPoint));
        yield return Require("SendRaw", typeof(Il2CppStructArray<byte>), typeof(int), typeof(int), typeof(IPEndPoint));
        yield return Require("SendRawCoreWithCleanup", typeof(Il2CppStructArray<byte>), typeof(int),
            typeof(int), typeof(IPEndPoint), typeof(NetPacket));
        yield return Require("SendRawCore", typeof(Il2CppStructArray<byte>), typeof(int), typeof(int), typeof(IPEndPoint));
    }

    private static MethodInfo Require(string name, params Type[] parameters) =>
        AccessTools.DeclaredMethod(typeof(LiteNetManager), name, parameters) ??
        throw new MissingMethodException(typeof(LiteNetManager).FullName, name);

    [HarmonyPrefix, HarmonyPriority(int.MaxValue)]
    internal static bool Prefix() => AuthHandler.ValidateAuthenticationSend();
}

[HarmonyPatch(typeof(LiteNetLib4MirrorClient), nameof(LiteNetLib4MirrorClient.ConnectClient), typeof(NetDataWriter))]
internal static class ClientConnectAdmissionGuard
{
    [HarmonyPrefix, HarmonyPriority(int.MaxValue)]
    internal static bool Prefix() => AuthHandler.ValidateAuthenticationSend();
}
