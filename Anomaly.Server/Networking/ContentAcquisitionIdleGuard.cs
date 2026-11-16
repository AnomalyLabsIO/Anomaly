using System;
using System.Reflection;
using HarmonyLib;

namespace Anomaly.Server.Networking;

/// <summary>Retains normal server scheduling while authenticated content has no player hub yet.</summary>
internal static class ContentAcquisitionIdleGuard
{
    internal static MethodBase FixedUpdateTarget => AccessTools.Method(typeof(IdleMode), "FixedUpdate");
    internal static MethodBase SetIdleModeTarget => AccessTools.Method(typeof(IdleMode), nameof(IdleMode.SetIdleMode),
        new[] { typeof(bool), typeof(bool) });

    [HarmonyPatch]
    private static class FixedUpdatePatch
    {
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() => FixedUpdateTarget ??
            throw new MissingMethodException("Stock idle-mode scheduling was not found.");

        // Stock FixedUpdate resets its elapsed timers before entering idle. Do
        // not consume that transition while content owns the busy reason; the
        // normal stock timers/settings apply as soon as the last owner retires.
        [HarmonyPrefix]
        private static bool Prefix() => !ConnectionContentBootstrap.HasPendingAcquisition;
    }

    [HarmonyPatch]
    private static class SetIdleModePatch
    {
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() => SetIdleModeTarget ??
            throw new MissingMethodException("Stock idle-mode transition was not found.");

        // Player removal and preauth callbacks can request idle without passing
        // FixedUpdate. Even forced idle must not throttle an owned acquisition.
        // Leaving idle and other owners' PauseIdleMode settings remain native.
        [HarmonyPrefix]
        private static bool Prefix(bool __0) => !__0 || !ConnectionContentBootstrap.HasPendingAcquisition;
    }
}
