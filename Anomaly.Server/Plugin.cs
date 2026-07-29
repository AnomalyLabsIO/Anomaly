using System;
using System.Linq;
using Anomaly.Server.Assets;
using Anomaly.Server.Auth;
using Anomaly.Server.Content;
using Anomaly.Server.Features;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Map;
using Anomaly.Server.Networking;
using Anomaly.Server.Round;
using Anomaly.Server.Utils;
using Anomaly.Shared.Networking;
using CentralAuth;
using HarmonyLib;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Loader;
using LabApi.Loader.Features.Plugins;

namespace Anomaly.Server;

public class Plugin : Plugin<Config>
{
    private const string OnlineModeConfigKey = "online_mode";

    public static Plugin Instance { get; private set; }
    public bool IsOperational { get; private set; }
    public Harmony Harmony { get; }
    private bool _configReloadSubscribed;
    private bool _lifecycleHandlersSubscribed;

    public Plugin()
    {
        Harmony = new Harmony("Anomaly.Server");
    }

    public override void Enable()
    {
        IsOperational = false;
        try
        {
            Logger.Info("Anomaly.Server enabling...");
            CustomNetworkManager.Modded = true;

            OffThreadLogger.Init();
            Logger.Info("OffThreadLogger initialized");

            EnforceOfflineMode();
            _configReloadSubscribed = true;
            GameCore.ConfigFile.OnConfigReloaded += EnforceOfflineMode;

            Instance = this;

            PasswordStore.Load();
            Logger.Info($"Password auth: required={PasswordStore.IsRequired}, verifierConfigured={PasswordStore.HasPassword}.");
            if (Config.RequirePassword && !PasswordStore.HasPassword)
                Logger.Warn("RequirePassword is enabled, but no Anomaly password verifier exists. Run 'password <value>' in LocalAdmin; clients will be rejected until then.");

            AnomalyMessageRegistry.Log = s => Logger.Info(s);
            AnomalyMessageRegistry.Warn = s => Logger.Warn(s);
            AnomalyMessageRegistry.RegisterDefaults();
            Logger.Info("AnomalyMessageRegistry initialized");

            ClientCapabilities.Init();
            PlayerColorBridge.Init();
            ContentCatalogBridge.Init();

            ServerAssetManager.Init(Config.AssetDirectory);
            AssetRegistry.LogSummary();
            AssetSpawner.Init();
            AssetOverrides.Init();
            Announcer.Init();
            AssetFileWatcher.Start(Config.AssetDirectory);

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

            RoundStateBroadcaster.Init();
            RoundStartBridge.Init();
            FacilityLayout.Init();

            Harmony.PatchAll();
            var patchedMethods = Harmony.GetPatchedMethods().ToList();
            Logger.Info($"Anomaly.Server enabled - {patchedMethods.Count} methods patched:");
            foreach (var method in patchedMethods)
            {
                Logger.Info($"  - {method.DeclaringType?.FullName}.{method.Name}");
            }

            IsOperational = true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Anomaly.Server failed to enable: {ex}");
            CleanupRuntime("failed enable");
        }
    }

    public override void Disable()
    {
        CleanupRuntime("disable");
    }

    public override string Name { get; } = "Anomaly";
    public override string Description { get; } = "Server plugin for the Anomaly modding framework.";
    public override string Author { get; } = "AnomalyLabs";
    public override Version Version => GetType().Assembly.GetName().Version;
    public override Version RequiredApiVersion => new Version(1, 1, 5);

    private static void OnAnomalyMessage(ReferenceHub hub, IAnomalyMessage message)
    {
        FileTransferSession.HandleMessage(hub, message);
    }

    private static void OnClientReady(ReferenceHub hub)
    {
        FileTransferSession.StartForPlayer(hub);
    }

    private static void OnPlayerLeft(
        LabApi.Events.Arguments.PlayerEvents.PlayerLeftEventArgs ev)
    {
        ReferenceHub hub = ev.Player.ReferenceHub;
        FileTransferSession.CleanupPlayer(hub);
        AnomalyReplayTable.Remove(hub);
        PlayerMotion.Reset(hub);
        ContentGrantGate.Remove(hub.netId);
    }

    private void CleanupRuntime(string context)
    {
        IsOperational = false;

        RunCleanup(context, "Harmony patches", () => Harmony.UnpatchAll());
        RunCleanup(context, "facility layout", FacilityLayout.Disable);
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
        RunCleanup(context, "asset spawner", AssetSpawner.Disable);
        RunCleanup(context, "server content registrar", ServerContentRegistrar.Reset);
        RunCleanup(context, "file transfer sessions", FileTransferSession.Reset);
        RunCleanup(context, "server asset manager", ServerAssetManager.Reset);
        RunCleanup(context, "content catalog bridge", ContentCatalogBridge.Disable);
        RunCleanup(context, "content registry", ContentRegistry.Reset);
        RunCleanup(context, "player color bridge", PlayerColorBridge.Disable);
        RunCleanup(context, "client capabilities", ClientCapabilities.Disable);
        RunCleanup(context, "player motion", PlayerMotion.ResetAll);
        RunCleanup(context, "replay table", AnomalyReplayTable.Reset);
        RunCleanup(context, "password store", PasswordStore.Reset);

        if (_configReloadSubscribed)
        {
            RunCleanup(context, "config reload handler", () =>
                GameCore.ConfigFile.OnConfigReloaded -= EnforceOfflineMode);
            _configReloadSubscribed = false;
        }

        Instance = null;
        AnomalyMessageRegistry.Log = _ => { };
        AnomalyMessageRegistry.Warn = _ => { };
        RunCleanup(context, "off-thread logger", OffThreadLogger.Disable);
    }

    private static void RunCleanup(string context, string component, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            Logger.Error($"Anomaly.Server {context} cleanup failed for {component}: {ex}");
        }
    }

    private static void EnforceOfflineMode()
    {
        try
        {
            PlayerAuthenticationManager.OnlineMode = false;
            CustomLiteNetLib4MirrorTransport.IpPassthroughEnabled = false;

            var serverConfig = GameCore.ConfigFile.ServerConfig;
            if (serverConfig == null)
            {
                Logger.Warn("Anomaly requires online_mode=false, but the gameplay config is not loaded yet. Runtime OnlineMode was still forced off.");
                return;
            }

            if (serverConfig.GetBool(OnlineModeConfigKey, true))
            {
                serverConfig.SetString(OnlineModeConfigKey, "false");
                Logger.Warn("Anomaly requires vanilla online_mode=false; updated config_gameplay online_mode to false.");
            }
            else
            {
                Logger.Info("OnlineMode forced to false for Anomaly auth.");
            }
        }
        catch (Exception ex)
        {
            PlayerAuthenticationManager.OnlineMode = false;
            CustomLiteNetLib4MirrorTransport.IpPassthroughEnabled = false;
            Logger.Error($"Failed to persist online_mode=false; runtime OnlineMode was forced off anyway: {ex}");
        }
    }
}
