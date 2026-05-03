using System;
using System.Linq;
using Anomaly.Server.Assets;
using Anomaly.Server.Auth;
using Anomaly.Server.Diagnostics;
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
    public Harmony Harmony { get; }

    public Plugin()
    {
        Harmony = new Harmony("Anomaly.Server");
    }

    public override void Enable()
    {
        try
        {
            Logger.Info("Anomaly.Server enabling...");
            CustomNetworkManager.Modded = true;

            OffThreadLogger.Init();
            Logger.Info("OffThreadLogger initialized");

            EnforceOfflineMode();
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

            PlayerColorBridge.Init();

            ServerAssetManager.Init(Config.AssetDirectory);
            AssetRegistry.LogSummary();
            AssetSpawner.Init();
            AssetOverrides.Init();
            Announcer.Init();
            AssetFileWatcher.Start(Config.AssetDirectory);

            AnomalyMessaging.MessageReceived += (hub, msg) =>
            {
                FileTransferSession.HandleMessage(hub, msg);
            };

            // Kick off the manifest + chunk send the instant the encrypted
            // channel is live, so the client isn't stuck staring at a loading
            // screen while we wait for any later lifecycle event.
            AnomalyMessaging.ClientReady += FileTransferSession.StartForPlayer;

            // Drop any in-flight session if the player disconnects between
            // chunks — otherwise ActiveSessions would leak an entry per drop.
            // Also clear the per-hub replay state so a new session starts fresh.
            PlayerEvents.Left += ev =>
            {
                FileTransferSession.CleanupPlayer(ev.Player.ReferenceHub);
                AnomalyReplayTable.Remove(ev.Player.ReferenceHub);
            };

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

            AuthAndRoleDiag.Init();
        }
        catch (Exception ex)
        {
            Logger.Error($"Anomaly.Server failed to enable: {ex}");
        }
    }

    public override void Disable()
    {
        GameCore.ConfigFile.OnConfigReloaded -= EnforceOfflineMode;
        AuthAndRoleDiag.Disable();
        RoundStartBridge.Disable();
        RoundStateBroadcaster.Disable();
        AssetFileWatcher.Stop();
        Harmony.UnpatchAll();

        Instance = null;
    }

    public override string Name { get; } = "Anomaly";
    public override string Description { get; } = "Server plugin for the Anomaly modding framework.";
    public override string Author { get; } = "AnomalyLabs";
    public override Version Version => GetType().Assembly.GetName().Version;
    public override Version RequiredApiVersion => new Version(1, 1, 5);

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
