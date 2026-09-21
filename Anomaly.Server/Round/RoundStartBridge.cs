using System;
using System.Reflection;
using HarmonyLib;
using LabApi.Events.Handlers;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Round
{
    /// <summary>
    /// Vanilla SCP:SL fires its server-side role-assignment pipeline by invoking
    /// <c>CharacterClassManager.OnRoundStarted</c> — a static event whose only
    /// publisher is the body of <c>UserCode_RpcRoundStarted</c>, the
    /// <c>[ClientRpc]</c> dispatch handler. On a vanilla dedicated server with
    /// a local <c>NetworkClient</c>, the [ClientRpc] dispatches locally and
    /// the event fires server-side as a side effect. In our setup the local
    /// dispatch never happens, so <c>RoleAssigner.OnRoundStarted</c> (and
    /// other subscribers like <c>SpectatorTargetTracker</c>,
    /// <c>Timed173PryableDoor</c>, <c>SkyboxHubert</c>) never run — players
    /// stay in <c>RoleTypeId.None</c> after round start.
    ///
    /// LabAPI's <c>ServerEvents.RoundStarted</c> fires reliably on the server
    /// (it's invoked unconditionally from <c>CharacterClassManager</c>'s round-
    /// start coroutine, two lines above the [ClientRpc] call). We piggyback
    /// it to invoke the vanilla static event reflectively, restoring the
    /// expected server-side fan-out.
    ///
    /// The LabAPI and native-RPC entry points share a per-round guard. When the
    /// native path is reached, its one-line multicast invocation is replaced by
    /// the same isolated fan-out used by the bridge. This keeps every vanilla
    /// subscriber independent and prevents the two entry points from dispatching
    /// the event twice in a single round.
    /// </summary>
    internal static class RoundStartBridge
    {
        private static bool _hooked;
        private static bool _raisedThisRound;
        private static FieldInfo _onRoundStartedField;

        internal static void Init()
        {
            if (_hooked) return;
            _hooked = true;

            _onRoundStartedField = ResolveBackingField();
            if (_onRoundStartedField == null)
            {
                _hooked = false;
                throw new MissingFieldException(
                    typeof(CharacterClassManager).FullName,
                    "OnRoundStarted");
            }

            _raisedThisRound = false;
            ServerEvents.WaitingForPlayers += ResetRoundGuard;
            ServerEvents.RoundRestarted += ResetRoundGuard;
            ServerEvents.RoundStarted += OnRoundStarted;
            Logger.Info("RoundStartBridge: bridging ServerEvents.RoundStarted -> CharacterClassManager.OnRoundStarted.");
        }

        internal static void Disable()
        {
            if (_hooked)
            {
                ServerEvents.WaitingForPlayers -= ResetRoundGuard;
                ServerEvents.RoundRestarted -= ResetRoundGuard;
                ServerEvents.RoundStarted -= OnRoundStarted;
                _hooked = false;
            }

            _raisedThisRound = false;
            _onRoundStartedField = null;
        }

        private static void OnRoundStarted()
        {
            DispatchOnce("LabAPI round-start bridge");
        }

        private static void ResetRoundGuard()
        {
            _raisedThisRound = false;
        }

        /// <summary>
        /// Harmony prefix for the native local-RPC callback. Return true only
        /// when the bridge is inactive so vanilla remains untouched after an
        /// owner-unpatch or retained-patch teardown.
        /// </summary>
        internal static bool InterceptNativeRoundStart()
        {
            if (!_hooked || _onRoundStartedField == null)
                return true;

            DispatchOnce("native round-start callback");
            return false;
        }

        private static void DispatchOnce(string source)
        {
            if (!_hooked || _onRoundStartedField == null || _raisedThisRound)
                return;

            _raisedThisRound = true;
            Action handler;
            try
            {
                handler = _onRoundStartedField.GetValue(null) as Action;
            }
            catch (Exception ex)
            {
                SafeLogError($"RoundStartBridge: could not read CharacterClassManager.OnRoundStarted from {source}: {ex}");
                return;
            }

            if (handler == null)
                return;

            foreach (Delegate subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action)subscriber)();
                }
                catch (Exception ex)
                {
                    SafeLogError($"RoundStartBridge: isolated CharacterClassManager.OnRoundStarted subscriber threw from {source}: {ex}");
                }
            }
        }

        private static void SafeLogError(string message)
        {
            try { Logger.Error(message); }
            catch { }
        }

        /// <summary>
        /// Compiler-generated backing field for <c>public static event Action OnRoundStarted</c>
        /// is a private static field with the same name on <c>CharacterClassManager</c>.
        /// </summary>
        private static FieldInfo ResolveBackingField()
        {
            return typeof(CharacterClassManager).GetField(
                "OnRoundStarted",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        }
    }

    [HarmonyPatch(typeof(CharacterClassManager), "UserCode_RpcRoundStarted")]
    internal static class NativeRoundStartDispatchPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return RoundStartBridge.InterceptNativeRoundStart();
        }
    }
}
