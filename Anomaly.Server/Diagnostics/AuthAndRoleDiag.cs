using System;
using System.Collections.Generic;
using System.Reflection;
using Anomaly.Server.Utils;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using MapGeneration;
using PlayerRoles;
using PlayerRoles.RoleAssign;
using RemoteAdmin;

namespace Anomaly.Server.Diagnostics
{
    /// <summary>
    /// Temporary instrumentation for the "RA empty / role stays in void" bugs.
    /// Logs the auth → permission → round-start → role-assign chain so we can
    /// see which step drops our hub. Uses LabAPI events wherever a hook exists
    /// and falls back to one Harmony patch for <c>QueryProcessor.ServerHandleCommandFromClient</c>
    /// (no LabAPI event covers the silent-drop on missing RA flag / rate limit).
    ///
    /// Remove this class once the bugs are diagnosed.
    /// </summary>
    internal static class AuthAndRoleDiag
    {
        private static bool _hooked;
        private static Harmony _harmony;

        internal static void Init()
        {
            if (_hooked) return;
            _hooked = true;

            PlayerEvents.Joined          += OnJoined;
            PlayerEvents.GroupChanged    += OnGroupChanged;
            PlayerEvents.ChangingRole    += OnChangingRole;
            PlayerEvents.ChangedRole     += OnChangedRole;
            PlayerEvents.Spawning        += OnSpawning;
            PlayerEvents.Spawned         += OnSpawned;
            ServerEvents.RoundStarted    += OnRoundStarted;
            ServerEvents.CommandExecuting += OnCommandExecuting;
            ServerEvents.MapGenerated    += OnMapGenerated;

            _harmony = new Harmony("Anomaly.Server.Diagnostics.AuthAndRoleDiag");
            TryPatch(typeof(QueryProcessor), "ServerHandleCommandFromClient",
                prefix: nameof(ServerHandleCommandFromClient_Prefix));
            TryPatch(typeof(RoleAssigner), "OnRoundStarted",
                prefix: nameof(RoleAssigner_OnRoundStarted_Prefix));
            TryPatch(typeof(RoleAssigner), "CheckPlayer",
                postfix: nameof(RoleAssigner_CheckPlayer_Postfix));
            TryPatch(typeof(PlayerRoleManager), nameof(PlayerRoleManager.ServerSetRole),
                prefix: nameof(ServerSetRole_Prefix));
            TryPatch(typeof(CharacterClassManager), "UserCode_RpcRoundStarted",
                prefix: nameof(UserCode_RpcRoundStarted_Prefix));
            TryPatch(typeof(AtlasZoneGenerator), "Generate",
                prefix: nameof(AtlasZoneGenerator_Generate_Prefix),
                postfix: nameof(AtlasZoneGenerator_Generate_Postfix));
            TryPatch(typeof(SeedSynchronizer), "GenerateLevel",
                postfix: nameof(SeedSynchronizer_GenerateLevel_Postfix));

            OffThreadLogger.Info("[Diag] AuthAndRoleDiag enabled.");
        }

        internal static void Disable()
        {
            if (!_hooked) return;
            _hooked = false;

            PlayerEvents.Joined          -= OnJoined;
            PlayerEvents.GroupChanged    -= OnGroupChanged;
            PlayerEvents.ChangingRole    -= OnChangingRole;
            PlayerEvents.ChangedRole     -= OnChangedRole;
            PlayerEvents.Spawning        -= OnSpawning;
            PlayerEvents.Spawned         -= OnSpawned;
            ServerEvents.RoundStarted    -= OnRoundStarted;
            ServerEvents.CommandExecuting -= OnCommandExecuting;
            ServerEvents.MapGenerated    -= OnMapGenerated;

            try { _harmony?.UnpatchAll("Anomaly.Server.Diagnostics.AuthAndRoleDiag"); }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] Unpatch failed: {ex}"); }
            _harmony = null;
        }

        // ---------------------------------------------------------------- LabAPI handlers

        private static void OnJoined(PlayerJoinedEventArgs ev)
        {
            try
            {
                var hub = ev.Player.ReferenceHub;
                OffThreadLogger.Info(
                    $"[Diag] PlayerJoined: userId={ev.Player.UserId} nick={ev.Player.Nickname} " +
                    $"mode={hub.authManager.InstanceMode} ra={hub.serverRoles.RemoteAdmin} " +
                    $"perms=0x{hub.serverRoles.Permissions:X16} group={ev.Player.GroupName ?? "<none>"}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] OnJoined: {ex}"); }
        }

        private static void OnGroupChanged(PlayerGroupChangedEventArgs ev)
        {
            try
            {
                var player = ev?.Player;
                if (player == null)
                {
                    OffThreadLogger.Warn("[Diag] GroupChanged: event player was null.");
                    return;
                }

                var hub = player.ReferenceHub;
                var serverRoles = hub?.serverRoles;
                var remoteAdmin = serverRoles != null ? serverRoles.RemoteAdmin.ToString() : "<null>";
                var permissions = serverRoles != null ? serverRoles.Permissions : 0UL;

                OffThreadLogger.Info(
                    $"[Diag] GroupChanged: userId={player.UserId ?? "<null>"} group={ev.Group?.BadgeText ?? "<null>"} " +
                    $"groupPerms=0x{(ev.Group?.Permissions ?? 0UL):X16} " +
                    $"ra={remoteAdmin} perms=0x{permissions:X16}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] OnGroupChanged: {ex}"); }
        }

        private static void OnChangingRole(PlayerChangingRoleEventArgs ev)
        {
            try
            {
                OffThreadLogger.Info(
                    $"[Diag] ChangingRole: userId={ev.Player.UserId} {ev.OldRole?.RoleTypeId.ToString() ?? "<null>"} -> {ev.NewRole} " +
                    $"reason={ev.ChangeReason} flags={ev.SpawnFlags} allowed={ev.IsAllowed}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] OnChangingRole: {ex}"); }
        }

        private static void OnChangedRole(PlayerChangedRoleEventArgs ev)
        {
            try
            {
                OffThreadLogger.Info(
                    $"[Diag] ChangedRole: userId={ev.Player.UserId} {ev.OldRole} -> {ev.NewRole?.RoleTypeId.ToString() ?? "<null>"} " +
                    $"reason={ev.ChangeReason}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] OnChangedRole: {ex}"); }
        }

        private static void OnSpawning(PlayerSpawningEventArgs ev)
        {
            try
            {
                OffThreadLogger.Info(
                    $"[Diag] Spawning: userId={ev.Player.UserId} role={ev.Role?.RoleTypeId.ToString() ?? "<null>"} allowed={ev.IsAllowed}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] OnSpawning: {ex}"); }
        }

        private static void OnSpawned(PlayerSpawnedEventArgs ev)
        {
            try
            {
                OffThreadLogger.Info(
                    $"[Diag] Spawned: userId={ev.Player.UserId} role={ev.Role?.RoleTypeId.ToString() ?? "<null>"}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] OnSpawned: {ex}"); }
        }

        private static void OnRoundStarted()
        {
            try
            {
                OffThreadLogger.Info("[Diag] RoundStarted — snapshot of all hubs:");
                foreach (var hub in ReferenceHub.AllHubs)
                {
                    if (hub == null) continue;
                    string role = "?";
                    try { role = hub.roleManager.CurrentRole?.RoleTypeId.ToString() ?? "<null>"; } catch { }
                    OffThreadLogger.Info(
                        $"[Diag]   hub: userId={hub.authManager.UserId ?? "<null>"} " +
                        $"mode={hub.authManager.InstanceMode} ra={hub.serverRoles.RemoteAdmin} " +
                        $"perms=0x{hub.serverRoles.Permissions:X16} role={role} " +
                        $"isLocalPlayer={hub.isLocalPlayer} netId={hub.networkIdentity?.netId ?? 0}");
                }
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] OnRoundStarted: {ex}"); }
        }

        private static void OnMapGenerated(MapGeneratedEventArgs ev)
        {
            try
            {
                int rooms = 0;
                try { foreach (var room in RoomIdentifier.AllRoomIdentifiers) { rooms++; } }
                catch (Exception ex) { OffThreadLogger.Warn($"[Diag] room enumeration failed: {ex.Message}"); }
                OffThreadLogger.Info(
                    $"[Diag] MapGenerated: seed={ev.Seed} rooms={rooms} mapGenerated={SeedSynchronizer.MapGenerated}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] OnMapGenerated: {ex}"); }
        }

        private static int _commandExecCount;
        private static void OnCommandExecuting(CommandExecutingEventArgs ev)
        {
            try
            {
                if (++_commandExecCount > 50 && _commandExecCount % 25 != 0) return;
                OffThreadLogger.Info(
                    $"[Diag] CommandExecuting #{_commandExecCount}: type={ev.CommandType} name='{ev.CommandName}' " +
                    $"found={ev.CommandFound} sender={ev.Sender?.LogName ?? "<null>"} allowed={ev.IsAllowed}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] OnCommandExecuting: {ex}"); }
        }

        // ---------------------------------------------------------------- Harmony patches

        private static void TryPatch(Type type, string methodName, string prefix = null, string postfix = null)
        {
            try
            {
                var target = AccessTools.Method(type, methodName);
                if (target == null)
                {
                    OffThreadLogger.Warn($"[Diag] {type.FullName}.{methodName} not found — diagnostic patch skipped.");
                    return;
                }
                var pre = prefix != null ? new HarmonyMethod(typeof(AuthAndRoleDiag), prefix) : null;
                var post = postfix != null ? new HarmonyMethod(typeof(AuthAndRoleDiag), postfix) : null;
                _harmony.Patch(target, prefix: pre, postfix: post);
                OffThreadLogger.Info($"[Diag] Patched {type.FullName}.{methodName}.");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] Failed to patch {type.FullName}.{methodName}: {ex}"); }
        }

        private static int _serverHandleCount;
        public static void ServerHandleCommandFromClient_Prefix(
            ReferenceHub hub,
            EncryptedChannelManager.EncryptedMessage content,
            EncryptedChannelManager.SecurityLevel securityLevel)
        {
            try
            {
                if (++_serverHandleCount > 50 && _serverHandleCount % 25 != 0) return;
                bool rate = false;
                try { rate = hub?.queryProcessor?._commandRateLimit?.CanExecute() ?? false; } catch { }
                bool ra = hub?.serverRoles?.RemoteAdmin ?? false;
                string text = content.ToString() ?? string.Empty;
                if (text.Length > 80) text = text.Substring(0, 80) + "...";
                OffThreadLogger.Info(
                    $"[Diag] ServerHandleCommandFromClient #{_serverHandleCount}: " +
                    $"userId={hub?.authManager?.UserId ?? "<null>"} ra={ra} rateOk={rate} " +
                    $"sec={securityLevel} content='{text}'");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] ServerHandleCommandFromClient_Prefix: {ex}"); }
        }

        public static void RoleAssigner_OnRoundStarted_Prefix()
        {
            OffThreadLogger.Info("[Diag] RoleAssigner.OnRoundStarted ENTERED.");
        }

        public static void RoleAssigner_CheckPlayer_Postfix(ReferenceHub hub, ref bool __result)
        {
            try
            {
                string role = "?";
                try { role = hub?.roleManager?.CurrentRole?.RoleTypeId.ToString() ?? "<null>"; } catch { }
                OffThreadLogger.Info(
                    $"[Diag] CheckPlayer: userId={hub?.authManager?.UserId ?? "<null>"} mode={hub?.authManager?.InstanceMode} " +
                    $"role={role} alive={hub?.IsAlive() ?? false} -> {__result}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] CheckPlayer_Postfix: {ex}"); }
        }

        public static void ServerSetRole_Prefix(PlayerRoleManager __instance, RoleTypeId newRole, RoleChangeReason reason)
        {
            try
            {
                string userId = __instance?._hub?.authManager?.UserId ?? "<null>";
                string oldRole = __instance?.CurrentRole?.RoleTypeId.ToString() ?? "<null>";
                OffThreadLogger.Info(
                    $"[Diag] ServerSetRole: userId={userId} {oldRole} -> {newRole} reason={reason}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] ServerSetRole_Prefix: {ex}"); }
        }

        public static void UserCode_RpcRoundStarted_Prefix()
        {
            OffThreadLogger.Info("[Diag] CharacterClassManager.UserCode_RpcRoundStarted ENTERED (server-local Rpc dispatch).");
        }

        public static void AtlasZoneGenerator_Generate_Prefix(AtlasZoneGenerator __instance)
        {
            try
            {
                int atlasCount = __instance?.Atlases?.Length ?? -1;
                int compatRooms = __instance?.CompatibleRooms?.Length ?? -1;
                OffThreadLogger.Info(
                    $"[Diag] AtlasZoneGenerator.Generate START: zone={__instance?.TargetZone} " +
                    $"atlases={atlasCount} compatibleRooms={compatRooms}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] Generate_Prefix: {ex}"); }
        }

        public static void AtlasZoneGenerator_Generate_Postfix(AtlasZoneGenerator __instance)
        {
            try
            {
                int spawnedCount = __instance?.Spawned?.Count ?? -1;
                OffThreadLogger.Info(
                    $"[Diag] AtlasZoneGenerator.Generate END: zone={__instance?.TargetZone} " +
                    $"spawnedRooms={spawnedCount}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] Generate_Postfix: {ex}"); }
        }

        public static void SeedSynchronizer_GenerateLevel_Postfix()
        {
            try
            {
                OffThreadLogger.Info(
                    $"[Diag] SeedSynchronizer.GenerateLevel END: MapGenerated={SeedSynchronizer.MapGenerated} " +
                    $"seed={SeedSynchronizer.Seed}");
            }
            catch (Exception ex) { OffThreadLogger.Error($"[Diag] GenerateLevel_Postfix: {ex}"); }
        }
    }
}
