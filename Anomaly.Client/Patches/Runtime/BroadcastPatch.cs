using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Fires <see cref="HudEvents.BroadcastShowing"/> when the local client
/// receives a broadcast RPC (<c>Broadcast.UserCode_RpcAddElement</c> /
/// <c>UserCode_TargetAddElement</c>). Cancelling skips the local render —
/// the broadcast text isn't added to the visible list on this client only.
///
/// Note: in the decompiled base game the <c>UserCode_*</c> bodies appear
/// empty, but they are the actual post-Mirror dispatch points the
/// RegisterRpc table routes to, and the real rendering logic lives there at
/// runtime. If the patch doesn't fire in practice, the alternative hook is
/// the static dispatcher <c>InvokeUserCode_RpcAddElement__String__UInt16__BroadcastFlags</c>.
/// </summary>
[HarmonyPatch]
public static class BroadcastPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Broadcast), "UserCode_RpcAddElement__String__UInt16__BroadcastFlags")]
    public static bool RpcAddElementPrefix(string data, ushort time, Broadcast.BroadcastFlags flags)
    {
        return DispatchAndCheck(data, time, flags, nameof(RpcAddElementPrefix));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Broadcast), "UserCode_TargetAddElement__NetworkConnection__String__UInt16__BroadcastFlags")]
    public static bool TargetAddElementPrefix(string data, ushort time, Broadcast.BroadcastFlags flags)
    {
        return DispatchAndCheck(data, time, flags, nameof(TargetAddElementPrefix));
    }

    private static bool DispatchAndCheck(string data, ushort time, Broadcast.BroadcastFlags flags, string context)
    {
        try
        {
            BroadcastShowingEventArgs ev = new(data, time, (byte)flags);
            HudEvents.OnBroadcastShowing(ev);
            return ev.IsAllowed;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"BroadcastPatch.{context}: {ex}");
            return true;
        }
    }
}
