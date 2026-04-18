using System;
using System.Linq;
using Anomaly.Server.API.Plugins;
using Anomaly.Server.Assets;
using Anomaly.Server.Diagnostics;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Map;
using Anomaly.Server.Networking;
using Anomaly.Server.Round;
using Anomaly.Server.Utils;
using Anomaly.Shared.Networking;
using CentralAuth;
using HarmonyLib;
using LabApi.Features.Console;
using LabApi.Loader;
using LabApi.Loader.Features.Plugins;

namespace Anomaly.Server;

public class Plugin : Plugin<Config>
{
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

            PlayerAuthenticationManager.OnlineMode = false;
            Logger.Info("OnlineMode set to false");

            Instance = this;

            AnomalyMessageRegistry.Log = s => Logger.Info(s);
            AnomalyMessageRegistry.Warn = s => Logger.Warn(s);
            AnomalyMessageRegistry.RegisterDefaults();
            Logger.Info("AnomalyMessageRegistry initialized");

            ServerAssetManager.Init(Config.AssetDirectory);
            AssetRegistry.LogSummary();
            AssetSpawner.Init();
            AssetOverrides.Init();
            AssetFileWatcher.Start(Config.AssetDirectory);

            AnomalyMessaging.MessageReceived += (hub, msg) =>
            {
                FileTransferSession.HandleMessage(hub, msg);
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

            // Discover [AnomalyPlugin]-attributed LabAPI plugins. Runs after Harmony
            // so any plugins loaded alongside us are already instantiated.
            AnomalyServerPlugins.Discover();
        }
        catch (Exception ex)
        {
            Logger.Error($"Anomaly.Server failed to enable: {ex}");
        }
    }

    public override void Disable()
    {
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
}