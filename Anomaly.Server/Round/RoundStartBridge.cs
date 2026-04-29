using System;
using System.Reflection;
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
    /// Re-firing is safe even if the local dispatch *does* happen on some
    /// builds: <c>RoleAssigner</c> guards against double-spawn via
    /// <c>CheckPlayer</c> rejecting hubs that are already alive.
    /// </summary>
    internal static class RoundStartBridge
    {
        private static bool _hooked;
        private static FieldInfo _onRoundStartedField;

        internal static void Init()
        {
            if (_hooked) return;
            _hooked = true;

            _onRoundStartedField = ResolveBackingField();
            if (_onRoundStartedField == null)
            {
                Logger.Warn("RoundStartBridge: CharacterClassManager.OnRoundStarted backing field not found — bridge inactive.");
                return;
            }

            ServerEvents.RoundStarted += OnRoundStarted;
            Logger.Info("RoundStartBridge: bridging ServerEvents.RoundStarted -> CharacterClassManager.OnRoundStarted.");
        }

        internal static void Disable()
        {
            if (!_hooked) return;
            _hooked = false;
            ServerEvents.RoundStarted -= OnRoundStarted;
        }

        private static void OnRoundStarted()
        {
            if (_onRoundStartedField == null) return;
            try
            {
                if (_onRoundStartedField.GetValue(null) is Action handler)
                    handler.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Error($"RoundStartBridge: re-firing CharacterClassManager.OnRoundStarted threw: {ex}");
            }
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
}
