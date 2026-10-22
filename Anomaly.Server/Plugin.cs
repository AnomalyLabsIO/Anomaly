using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Anomaly.Server.Assets;
using Anomaly.Server.Api.Assets;
using Anomaly.Server.Api.Features;
using Anomaly.Server.Auth;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Lifecycle;
using Anomaly.Server.Internal.Map;
using Anomaly.Server.Networking;
using Anomaly.Server.Patches;
using Anomaly.Server.Round;
using Anomaly.Server.Utils;
using Anomaly.Shared.Networking;
using CentralAuth;
using HarmonyLib;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Features;
using LabApi.Loader;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;

namespace Anomaly.Server;

public class Plugin : Plugin<Config>
{
    private const string HarmonyOwnerId = ServerLifecycleTransaction.AnomalyHarmonyOwnerId;

    public static Plugin Instance { get; private set; }
    public bool IsOperational { get; private set; }
    internal static bool IsAdmissionOpen
    {
        get
        {
            var plugin = Instance;
            if (plugin == null)
                return false;
            lock (plugin._admissionSync)
                return ReferenceEquals(Instance, plugin) && plugin.IsOperational;
        }
    }
    internal Harmony Harmony { get; }
    private bool _configReloadSubscribed;
    private bool _lifecycleHandlersSubscribed;
    private bool _harmonyPatched;
    private bool _runtimeSnapshotCaptured;
    private bool _runtimeOverridesApplied;
    private bool _teardownStarted;
    private bool _retainedAcceptedHandoffBarrier;
    private bool _retainedAuthenticatedDisconnectBarrier;
    private RuntimeStateSnapshot _runtimeState;
    private readonly object _admissionSync = new object();

    public Plugin()
    {
        Harmony = new Harmony(HarmonyOwnerId);
    }

    public override void Enable()
    {
        if (IsOperational) return;
        if (MapGeneration.SeedSynchronizer.MapGenerated)
        {
            SafeLogError("Anomaly.Server must be enabled before facility generation. Restart the server to load or replace Anomaly; the current map has no authenticated snapshot.");
            return;
        }
        Plugin existing = Instance;
        if (existing != null && !ReferenceEquals(existing, this) && existing._harmonyPatched)
        {
            try
            {
                Logger.Error(
                    "Anomaly.Server cannot replace a retained fail-closed plugin instance. " +
                    "Restart the server after the teardown barrier is reported.");
            }
            catch { }
            return;
        }
        if (ReferenceEquals(existing, this) && _harmonyPatched && _teardownStarted)
        {
            try
            {
                Logger.Error(
                    "Anomaly.Server cannot re-enable while fail-closed teardown patches are retained. " +
                    "Restart the server.");
            }
            catch { }
            return;
        }

        lock (_admissionSync)
        {
            _teardownStarted = false;
            IsOperational = false;
        }
        Instance = this;
        ServerLifecycleTransaction.Execute(
            CreateInitializationStages(),
            closeAdmission: CloseAdmission,
            commitOperational: () =>
            {
                lock (_admissionSync)
                {
                    if (_teardownStarted)
                        throw new InvalidOperationException("Anomaly teardown began before enablement committed.");
                    IsOperational = true;
                }
                Logger.Info("Anomaly.Server enabled; authenticated admission is operational.");
            },
            rollback: ex =>
            {
                SafeLogError($"Anomaly.Server failed to enable: {ex}");
                CleanupRuntime("failed enable");
            });
    }

    private IReadOnlyDictionary<ServerInitializationStage, Action> CreateInitializationStages()
    {
        return new Dictionary<ServerInitializationStage, Action>
        {
            [ServerInitializationStage.CaptureRuntimeState] = () =>
            {
                Logger.Info("Anomaly.Server enabling...");
                if (!_runtimeSnapshotCaptured)
                    CaptureRuntimeState();
            },
            [ServerInitializationStage.OffThreadLogging] = () =>
            {
                OffThreadLogger.Init();
                Logger.Info("OffThreadLogger initialized");
            },
            [ServerInitializationStage.IdentityAndAuthentication] = () =>
            {
                if (!ServerIdentityStore.LoadOrCreate(out var identityError))
                    throw new InvalidOperationException("Server signing identity is unavailable: " + identityError);
                Logger.Info($"Application protocol v{AnomalyProtocol.CurrentVersion} server identity fingerprint: {ServerIdentityStore.Fingerprint}");
                if (!AcceptedClientIdentityStore.Initialize(out var acceptedIdentityError))
                    throw new InvalidOperationException(acceptedIdentityError);
                PreauthGate.Initialize();
                AuthHandler.Reset();
                EnrollmentSessionRegistry.Reset();
                ClientModAdmissionPolicy.Configure(Config, Version);
            },
            [ServerInitializationStage.MandatoryPatches] = () =>
            {
                // Install the admission guard before initializing optional/content
                // services. IsOperational remains false, so a request racing a live
                // reload is rejected instead of falling through to vanilla offline
                // admission during the initialization window.
                if (!_harmonyPatched)
                {
                    _harmonyPatched = true;
                    Harmony.PatchAll();
                }
                VerifyMandatoryPatches();
                var patchedMethods = Harmony.GetPatchedMethods().ToList();
                Logger.Info($"Anomaly.Server prepared - {patchedMethods.Count} methods patched:");
                foreach (var method in patchedMethods)
                    Logger.Info($"  - {method.DeclaringType?.FullName}.{method.Name}");
            },
            [ServerInitializationStage.PasswordPolicy] = () =>
            {
                PasswordStore.Load();
                Logger.Info($"Password auth: required={PasswordStore.IsRequired}, verifierConfigured={PasswordStore.HasPassword}.");
                if (Config.RequirePassword && !PasswordStore.HasPassword)
                    Logger.Warn("RequirePassword is enabled, but no Anomaly password verifier exists. Run 'password <value>' in LocalAdmin; clients will be rejected until then.");
                if (Config.RequirePassword && !Config.EnableLegacyPasswordAuthOnLoopback)
                    Logger.Warn("RequirePassword is enabled, but public password mode is disabled because no vetted OPAQUE implementation supports both target runtimes. Remote clients will be rejected; the legacy loopback-only override is migration-only.");
            },
            [ServerInitializationStage.MessageRegistry] = () =>
            {
                AnomalyMessageRegistry.Log = s => Logger.Info(s);
                AnomalyMessageRegistry.Warn = s => Logger.Warn(s);
                AnomalyMessageRegistry.RegisterDefaults();
                Logger.Info("AnomalyMessageRegistry initialized");
            },
            [ServerInitializationStage.FeatureBridges] = () =>
            {
                ClientCapabilities.Init();
                Poses.PlayerPoseRuntime.Init();
                ContentCatalogBridge.Init();
            },
            [ServerInitializationStage.AssetsAndContent] = () =>
            {
                ServerAssetManager.Init(Config.AssetDirectory);
                AssetOverrides.Init();
                DecorativeVisualScope.Init();
                Announcer.Init();
                AssetFileWatcher.Start(Config.AssetDirectory);
            },
            [ServerInitializationStage.LifecycleSubscriptions] = () =>
            {
                _lifecycleHandlersSubscribed = true;
                AnomalyMessaging.MessageReceived += OnAnomalyMessage;

                // Kick off the manifest + chunk send the instant the encrypted
                // channel is live, so the client isn't stuck staring at a loading
                // screen while we wait for any later lifecycle event.
                AnomalyMessaging.ClientReady += OnClientReady;

                // Drop any in-flight session if the player disconnects between
                // chunks — otherwise ActiveSessions would leak an entry per drop.
                // Also clear the per-hub replay state so a new session starts fresh.
                PlayerEvents.Left += OnPlayerLeft;
            },
            [ServerInitializationStage.RoundAndFacility] = () =>
            {
                RoundStateBroadcaster.Init();
                RoundStartBridge.Init();
                ConnectionBootstrap.Init();
                FacilityLayoutRuntime.Init();
            },
            [ServerInitializationStage.RuntimeOverrides] = ApplyRuntimeOverrides,
            [ServerInitializationStage.ConfigReloadSubscription] = () =>
            {
                GameCore.ConfigFile.OnConfigReloaded += ReapplyRuntimeOverrides;
                _configReloadSubscribed = true;
            },
        };
    }

    private void CloseAdmission()
    {
        lock (_admissionSync)
            IsOperational = false;
    }

    public override void Disable()
    {
        CleanupRuntime("disable");
    }

    public override string Name { get; } = "Anomaly";
    public override string Description { get; } = "Server plugin for the Anomaly modding framework.";
    public override string Author { get; } = "AnomalyLabs";
    public override Version Version => GetType().Assembly.GetName().Version;
    public override Version RequiredApiVersion => new Version(LabApiProperties.CompiledVersion);
    public override LoadPriority Priority => LoadPriority.Highest;

    private static void OnAnomalyMessage(ReferenceHub hub, IAnomalyMessage message)
    {
        FileTransferSession.HandleMessage(hub, message);
    }

    private static void OnClientReady(ReferenceHub hub)
    {
        try
        {
            string enrollmentId = hub?.authManager?.UserId;
            if (string.IsNullOrWhiteSpace(enrollmentId) ||
                !enrollmentId.EndsWith("@anomaly", StringComparison.Ordinal))
                throw new InvalidOperationException("Authenticated enrollment identity is unavailable.");

            if (!AnomalyMessaging.TrySendToClient(hub, new Anomaly.Shared.Networking.Messages.EnrollmentIdentityMessage
            {
                EnrollmentId = enrollmentId,
            }))
                throw new InvalidOperationException("The authenticated enrollment identity could not be delivered.");
            FileTransferSession.StartForPlayer(
                hub,
                (manifestSha256, snapshot) => ContentCatalogBridge.OfferForPlayer(hub, manifestSha256, snapshot));
        }
        catch (Exception exception)
        {
            CleanupPlayerState(hub, "client-ready-failure", quarantineTransport: true);
            try
            {
                if (hub?.connectionToClient == null)
                    throw new InvalidOperationException("The authenticated connection was unavailable.");
                hub.connectionToClient.Disconnect();
            }
            catch (Exception disconnectException)
            {
                try { Logger.Error($"Failed to disconnect after client-ready setup failed: {disconnectException}"); }
                catch { }
            }
            try
            {
                Logger.Error($"Failed to prepare authenticated content state for {hub?.LoggedNameFromRefHub()}: {exception}");
            }
            catch { }
        }
    }

    private static void OnPlayerLeft(
        LabApi.Events.Arguments.PlayerEvents.PlayerLeftEventArgs ev)
    {
        ReferenceHub hub = ev?.Player?.ReferenceHub;
        // Fast restart destroys player objects while retaining Mirror connections.
        // Keep the connection-owned keys/replay counters for the next authenticated
        // facility snapshot; normal disconnect still revokes them in its own hook.
        var connection = hub?.connectionToClient;
        if (Instance?.IsOperational == true && RoundRestarting.RoundRestart.IsRoundRestarting &&
            connection != null && Mirror.NetworkServer.connections.TryGetValue(connection.connectionId, out var current) &&
            ReferenceEquals(connection, current))
            AuthenticatedTransportSessions.TryReleaseHubForRestart(hub, PlayerAuthenticationManager.AllowSameAccountJoining);
        CleanupPlayerState(hub, "player-left");
    }

    internal static void QuarantineAuthenticatedPlayer(ReferenceHub hub, string context)
    {
        CleanupPlayerState(hub, context, quarantineTransport: true);
    }

    private void CleanupRuntime(string context)
    {
        if (Instance != null && !ReferenceEquals(Instance, this))
            return;

        // Serialize the close transition with the final StorePlayer + Accept
        // operation in ProcessConnectionRequest. Once this lock is released no
        // in-flight phase-two request can newly enter Mirror during teardown.
        lock (_admissionSync)
        {
            _teardownStarted = true;
            IsOperational = false;
        }

        int acceptedPendingPeers = AuthHandler.DrainStoredPlayers();
        if (acceptedPendingPeers != 0)
            _retainedAcceptedHandoffBarrier = true;
        bool acceptedHandoffsDrained = !_retainedAcceptedHandoffBarrier;
        if (!acceptedHandoffsDrained)
        {
            SafeLogError(
                $"Anomaly.Server drained {acceptedPendingPeers} accepted authentication handoff(s) during {context}; " +
                "Harmony admission/authentication patches will remain installed so those peers are rejected before " +
                "vanilla authentication can observe them. A server restart is required before re-enabling Anomaly.");
        }

        bool currentDisconnectsSucceeded = DisconnectAuthenticatedClients(context);
        if (!currentDisconnectsSucceeded)
            _retainedAuthenticatedDisconnectBarrier = true;
        bool authenticatedClientsDisconnected = !_retainedAuthenticatedDisconnectBarrier;

        if (_configReloadSubscribed)
        {
            RunCleanup(context, "config reload handler", () =>
                GameCore.ConfigFile.OnConfigReloaded -= ReapplyRuntimeOverrides);
            _configReloadSubscribed = false;
        }

        if (!authenticatedClientsDisconnected)
        {
            SafeLogError(
                "Anomaly.Server retained its patches and runtime overrides because one or more authenticated " +
                "clients could not be disconnected safely during teardown.");
        }
        Func<bool> restoreRuntimeState = () =>
            acceptedHandoffsDrained &&
            authenticatedClientsDisconnected &&
            TryRestoreRuntimeState(context);
        if (_harmonyPatched)
        {
            bool ownerPatchesRemoved = ServerLifecycleRollbackPolicy.TryRestoreAndOwnerUnpatch(
                restoreRuntimeState,
                () => TryRunCleanup(context, "Harmony patches", () => Harmony.UnpatchAll(HarmonyOwnerId)),
                () => SafeLogError(
                    "Anomaly.Server retained its deny-all admission patch because runtime auth restoration or " +
                    "owner-scoped unpatching did not complete safely."),
                (operation, exception) => SafeLogError(
                    $"Anomaly.Server {context} lifecycle rollback failed during {operation}: {exception}"));
            if (ownerPatchesRemoved)
                _harmonyPatched = false;
        }
        else
            _ = restoreRuntimeState();

        RunCleanup(context, "connection bootstrap", ConnectionBootstrap.Disable);
        RunCleanup(context, "facility layout", FacilityLayoutRuntime.Disable);
        RunCleanup(context, "round start bridge", RoundStartBridge.Disable);
        RunCleanup(context, "round state broadcaster", RoundStateBroadcaster.Disable);

        if (_lifecycleHandlersSubscribed)
        {
            RunCleanup(context, "message handler", () =>
                AnomalyMessaging.MessageReceived -= OnAnomalyMessage);
            RunCleanup(context, "client-ready handler", () =>
                AnomalyMessaging.ClientReady -= OnClientReady);
            RunCleanup(context, "player-left handler", () =>
                PlayerEvents.Left -= OnPlayerLeft);
            _lifecycleHandlersSubscribed = false;
        }

        RunCleanup(context, "asset file watcher", AssetFileWatcher.Stop);
        RunCleanup(context, "announcer", Announcer.Disable);
        RunCleanup(context, "asset overrides", AssetOverrides.Disable);
        RunCleanup(context, "decorative visuals", DecorativeVisualScope.Disable);
        RunCleanup(context, "server content registrar", () =>
        {
            if (!ServerContentRegistrar.Reset())
                throw new InvalidOperationException("One or more server item templates remain pending cleanup.");
        });
        RunCleanup(context, "file transfer sessions", FileTransferSession.Reset);
        RunCleanup(context, "server asset manager", ServerAssetManager.Reset);
        RunCleanup(context, "content catalog bridge", ContentCatalogBridge.Disable);
        RunCleanup(context, "content registry", ContentRegistry.Reset);
        RunCleanup(context, "client capabilities", ClientCapabilities.Disable);
        RunCleanup(context, "player poses", Poses.PlayerPoseRuntime.Disable);
        RunCleanup(context, "player motion", PlayerMotion.ResetAll);
        RunCleanup(context, "replay table", AnomalyReplayTable.Reset);
        RunCleanup(context, "authenticated transport sessions", AuthenticatedTransportSessions.Reset);
        RunCleanup(context, "auth state", AuthHandler.Reset);
        RunCleanup(context, "client mod admission policy", ClientModAdmissionPolicy.Reset);
        RunCleanup(context, "enrollment sessions", EnrollmentSessionRegistry.Reset);
        RunCleanup(context, "preauth gate", PreauthGate.Reset);
        RunCleanup(context, "password store", PasswordStore.Reset);
        RunCleanup(context, "accepted client identities", AcceptedClientIdentityStore.Reset);
        RunCleanup(context, "server identity", ServerIdentityStore.Reset);

        // A retained patch needs the plugin instance so its operational guard
        // continues to reject instead of falling through to vanilla offline auth.
        if (!_harmonyPatched)
            Instance = null;
        AnomalyMessageRegistry.Log = _ => { };
        AnomalyMessageRegistry.Warn = _ => { };
        RunCleanup(context, "off-thread logger", OffThreadLogger.Disable);
    }

    private static bool DisconnectAuthenticatedClients(string context)
    {
        bool succeeded = true;
        ReferenceHub[] hubs = AuthenticatedTransportSessions.SnapshotHubs();
        foreach (ReferenceHub hub in hubs)
        {
            if (hub == null)
                continue;

            bool disconnectInitiated = false;
            if (hub.connectionToClient == null)
            {
                SafeLogError(
                    $"Anomaly.Server {context} could not verify disconnect initiation for an authenticated client " +
                    "because its connection was unavailable.");
                succeeded = false;
            }
            else if (!TryRunCleanup(context, "authenticated client disconnect", () =>
                         hub.connectionToClient.Disconnect()))
            {
                succeeded = false;
            }
            else
            {
                disconnectInitiated = true;
            }
            CleanupPlayerState(
                hub,
                context + "-authenticated-client",
                quarantineTransport: !disconnectInitiated);
        }
        foreach (var connection in AuthenticatedTransportSessions.SnapshotConnections())
        {
            AuthenticatedTransportSessions.QuarantineConnection(connection);
            if (TryRunCleanup(context, "pending authenticated connection disconnect", connection.Disconnect))
                AuthenticatedTransportSessions.RemoveConnection(connection);
            else
                succeeded = false;
        }
        return succeeded;
    }

    private static void CleanupPlayerState(
        ReferenceHub hub,
        string context,
        bool quarantineTransport = false)
    {
        if (hub == null)
            return;

        // Revoke logical/security ownership before best-effort gameplay cleanup.
        RunCleanup(context, "authenticated transport session", () =>
        {
            if (quarantineTransport)
                AuthenticatedTransportSessions.Quarantine(hub);
            else
                AuthenticatedTransportSessions.Remove(hub);
        });
        RunCleanup(context, "enrollment session", () => EnrollmentSessionRegistry.Remove(hub));
        RunCleanup(context, "content grant", () => ContentGrantGate.Remove(hub.netId));
        RunCleanup(context, "file transfer session", () => FileTransferSession.CleanupPlayer(hub));
        RunCleanup(context, "replay table", () => AnomalyReplayTable.Remove(hub));
        RunCleanup(context, "player motion", () => PlayerMotion.Reset(hub));
        RunCleanup(context, "player poses", () => Poses.PlayerPoseRuntime.RemovePlayer(hub));
    }

    private static void RunCleanup(string context, string component, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            try { Logger.Error($"Anomaly.Server {context} cleanup failed for {component}: {ex}"); }
            catch { }
        }
    }

    private static bool TryRunCleanup(string context, string component, Action cleanup)
    {
        try
        {
            cleanup();
            return true;
        }
        catch (Exception ex)
        {
            try { Logger.Error($"Anomaly.Server {context} cleanup failed for {component}: {ex}"); }
            catch { }
            return false;
        }
    }

    private static void SafeLogError(string message)
    {
        try { Logger.Error(message); }
        catch { }
    }

    private void CaptureRuntimeState()
    {
        _runtimeState = new RuntimeStateSnapshot(
            PlayerAuthenticationManager.OnlineMode,
            CustomLiteNetLib4MirrorTransport.IpPassthroughEnabled,
            CustomNetworkManager.Modded);
        _runtimeSnapshotCaptured = true;
    }

    private void ApplyRuntimeOverrides()
    {
        // Set before the individual assignments so even a partial failure is
        // restored by the transactional cleanup path.
        _runtimeOverridesApplied = true;
        PlayerAuthenticationManager.OnlineMode = false;
        CustomLiteNetLib4MirrorTransport.IpPassthroughEnabled = false;
        CustomNetworkManager.Modded = true;
        Logger.Info("Applied runtime-only Anomaly authentication state; config_gameplay was not modified.");
    }

    private void ReapplyRuntimeOverrides()
    {
        Exception failure = null;
        lock (_admissionSync)
        {
            if (!_runtimeOverridesApplied || _teardownStarted)
                return;
            try
            {
                PlayerAuthenticationManager.OnlineMode = false;
                CustomLiteNetLib4MirrorTransport.IpPassthroughEnabled = false;
                CustomNetworkManager.Modded = true;
            }
            catch (Exception ex)
            {
                // The admission prefix consumes requests while non-operational, so a
                // config reload can never expose vanilla offline admission on failure.
                IsOperational = false;
                failure = ex;
            }
        }

        if (failure == null)
            return;

        try { Logger.Error($"Failed to reapply runtime-only Anomaly auth state; admission is now closed: {failure}"); }
        catch { }
        CleanupRuntime("runtime override reapply failure");
    }

    private bool TryRestoreRuntimeState(string context)
    {
        if (!_runtimeSnapshotCaptured)
            return true;

        if (!_runtimeOverridesApplied)
        {
            // This enable attempt never took ownership of the runtime values.
            // Discard its observation so a retry captures the then-current state
            // instead of restoring a stale snapshot on a later disable.
            _runtimeSnapshotCaptured = false;
            _runtimeState = default(RuntimeStateSnapshot);
            return true;
        }
        try
        {
            PlayerAuthenticationManager.OnlineMode = _runtimeState.OnlineMode;
            CustomLiteNetLib4MirrorTransport.IpPassthroughEnabled = _runtimeState.IpPassthroughEnabled;
            CustomNetworkManager.Modded = _runtimeState.Modded;
            _runtimeOverridesApplied = false;
            _runtimeSnapshotCaptured = false;
            _runtimeState = default(RuntimeStateSnapshot);
            try { Logger.Info("Restored pre-Anomaly runtime authentication state."); }
            catch { }
            return true;
        }
        catch (Exception ex)
        {
            try { Logger.Error($"Anomaly.Server {context} failed to restore runtime authentication state: {ex}"); }
            catch { }
            return false;
        }
    }

    private static void VerifyMandatoryPatches()
    {
        var required = new List<MethodBase>
        {
            AccessTools.Method(typeof(MapGeneration.SeedSynchronizer), "Awake"),
            AccessTools.Method(typeof(MapGeneration.SeedSynchronizer), "GenerateLevel"),
            AccessTools.Method(typeof(MapGeneration.SeedSynchronizer), "OnNewPlayerConnected"),
            AccessTools.Method(typeof(MapGeneration.AtlasZoneGenerator), "Generate"),
            AccessTools.Method(typeof(Mirror.NetworkServer), "OnConnected"),
            AccessTools.Method(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnServerDisconnect)),
            AccessTools.Method(
                typeof(CustomLiteNetLib4MirrorTransport),
                nameof(CustomLiteNetLib4MirrorTransport.ProcessConnectionRequest)),
            AccessTools.Method(
                typeof(PlayerAuthenticationManager),
                nameof(PlayerAuthenticationManager.FixedUpdate)),
            AccessTools.Method(
                typeof(EncryptedChannelManager),
                nameof(EncryptedChannelManager.ServerReceivePackedMessage)),
            AccessTools.Method(
                typeof(EncryptedChannelManager),
                nameof(EncryptedChannelManager.TrySendMessageToClient),
                new[] { typeof(byte[]), typeof(EncryptedChannelManager.EncryptedChannel) }),
            AccessTools.Method(
                typeof(EncryptedChannelManager),
                nameof(EncryptedChannelManager.TrySendMessageToClient),
                new[] { typeof(string), typeof(EncryptedChannelManager.EncryptedChannel) }),
            AccessTools.Method(
                typeof(CharacterClassManager),
                "UserCode_RpcRoundStarted"),
        };

        foreach (var method in required)
        {
            if (method == null)
                throw new MissingMethodException("A mandatory Anomaly patch target was not found.");
            var patchInfo = HarmonyLib.Harmony.GetPatchInfo(method);
            if (patchInfo == null || !patchInfo.Owners.Contains(HarmonyOwnerId))
            {
                throw new InvalidOperationException(
                    $"Mandatory Anomaly patch was not installed: {method.DeclaringType?.FullName}.{method.Name}.");
            }
        }
    }

    internal static bool TryCommitAuthenticatedAdmission(Action commit)
    {
        if (commit == null)
            throw new ArgumentNullException(nameof(commit));

        var plugin = Instance;
        if (plugin == null)
            return false;
        lock (plugin._admissionSync)
        {
            if (!ReferenceEquals(Instance, plugin) || !plugin.IsOperational)
                return false;
            commit();
            return true;
        }
    }

    internal static bool IsAuthenticatedAdmissionCurrent(ReferenceHub hub)
    {
        var plugin = Instance;
        if (plugin == null || hub == null)
            return false;
        lock (plugin._admissionSync)
        {
            return ReferenceEquals(Instance, plugin) &&
                   plugin.IsOperational &&
                   AuthenticatedTransportSessions.IsActive(hub);
        }
    }

    private readonly struct RuntimeStateSnapshot
    {
        public RuntimeStateSnapshot(bool onlineMode, bool ipPassthroughEnabled, bool modded)
        {
            OnlineMode = onlineMode;
            IpPassthroughEnabled = ipPassthroughEnabled;
            Modded = modded;
        }

        public bool OnlineMode { get; }
        public bool IpPassthroughEnabled { get; }
        public bool Modded { get; }
    }
}
