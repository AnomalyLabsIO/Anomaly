using System;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppPlayerRoles;
using Il2CppPlayerRoles.PlayableScps.Scp079;
using Il2CppPlayerRoles.Spectating;
using MelonLoader;

namespace Anomaly.Client.Internal.Players;

/// <summary>Owns native spectator delegates and client-received SCP-079 observations.</summary>
internal static class PlayerObservationBridge
{
    private static readonly TransactionalInitializer Initializer = new();
    private static Il2CppSystem.Action _targetChanged;
    private static PlayerRoleManager.RoleChanged _roleChanged;
    private static Player _previousTarget;

    internal static void Initialize()
    {
        if (Initializer.IsInitialized)
            return;

        _targetChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(HandleTargetChanged));
        _roleChanged = DelegateSupport.ConvertDelegate<PlayerRoleManager.RoleChanged>(
            new Action<ReferenceHub, PlayerRoleBase, PlayerRoleBase>(HandleRoleChanged));

        Initializer.Initialize(
            new InitializationStep(
                () => SpectatorTargetTracker.OnTargetChanged += _targetChanged,
                () => SpectatorTargetTracker.OnTargetChanged -= _targetChanged),
            new InitializationStep(
                () => PlayerRoleManager.OnRoleChanged += _roleChanged,
                () => PlayerRoleManager.OnRoleChanged -= _roleChanged),
            new InitializationStep(() => _previousTarget = Spectating.CurrentPlayer, Reset));
    }

    internal static void Shutdown()
    {
        Initializer.Reset();
        _targetChanged = null;
        _roleChanged = null;
        Reset();
    }

    /// <summary>Clears session observations without removing the process-lifetime hooks.</summary>
    internal static void Reset()
    {
        _previousTarget = null;
        Scp079.ClearAll();
    }

    private static void HandleTargetChanged()
    {
        if (!Initializer.IsInitialized)
            return;

        try
        {
            Scp079.InvalidateSpectatorPower();
            Player current = Spectating.CurrentPlayer;
            Player previous = _previousTarget;
            _previousTarget = current;
            if (ReferenceEquals(previous, current) ||
                (previous != null && current != null && previous.ReferenceHub == current.ReferenceHub))
                return;

            SpectatorEvents.OnTargetChanged(new SpectatedPlayerChangedEventArgs(LocalPlayer.Instance, previous, current));
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"PlayerObservationBridge: spectator target observation failed: {ex}");
        }
    }

    private static void HandleRoleChanged(ReferenceHub hub, PlayerRoleBase previous, PlayerRoleBase current)
    {
        if (!Initializer.IsInitialized)
            return;

        try
        {
            Scp079.Unregister(previous?.TryCast<Scp079Role>());
            if (hub != null && hub.isLocalPlayer)
                Scp079.InvalidateSpectatorPower();
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"PlayerObservationBridge: role observation cleanup failed: {ex}");
        }
    }

    [HarmonyPatch(typeof(Scp079AuxManager), nameof(Scp079AuxManager.ClientProcessRpc))]
    private static class AuxiliaryPowerRpcPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Scp079AuxManager __instance)
        {
            if (!Initializer.IsInitialized)
                return;

            try { Scp079.ObserveAuxiliaryPower(__instance); }
            catch (Exception ex)
            {
                MelonLogger.Warning($"PlayerObservationBridge: SCP-079 auxiliary power observation failed: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Scp079TierManager), nameof(Scp079TierManager.ClientProcessRpc))]
    private static class ExperienceRpcPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Scp079TierManager __instance)
        {
            if (!Initializer.IsInitialized)
                return;

            try { Scp079.ObserveExperience(__instance); }
            catch (Exception ex)
            {
                MelonLogger.Warning($"PlayerObservationBridge: SCP-079 experience observation failed: {ex}");
            }
        }
    }
}
