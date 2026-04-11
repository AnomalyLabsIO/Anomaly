using System;
using System.Linq;
using Anomaly.Server.Networking;
using Anomaly.Server.Utils;
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

            OffThreadLogger.Init();
            Logger.Info("OffThreadLogger initialized");

            PlayerAuthenticationManager.OnlineMode = false;
            Logger.Info("OnlineMode set to false");

            Instance = this;

            AnomalyMessageRegistry.RegisterAll();
            Logger.Info("AnomalyMessageRegistry initialized");

            Harmony.PatchAll();
            var patchedMethods = Harmony.GetPatchedMethods().ToList();
            Logger.Info($"Anomaly.Server enabled - {patchedMethods.Count} methods patched:");
            foreach (var method in patchedMethods)
            {
                Logger.Info($"  - {method.DeclaringType?.FullName}.{method.Name}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Anomaly.Server failed to enable: {ex}");
        }
    }

    public override void Disable()
    {
        Harmony.UnpatchAll();

        Instance = null;
    }

    public override string Name { get; } = "Anomaly";
    public override string Description { get; } = "Server plugin for the Anomaly modding framework.";
    public override string Author { get; } = "AnomalyLabs";
    public override Version Version => GetType().Assembly.GetName().Version;
    public override Version RequiredApiVersion => new Version(1, 1, 5);
}