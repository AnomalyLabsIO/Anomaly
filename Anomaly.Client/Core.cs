using System.Reflection;
using Anomaly.Client.API.Localization;
using Anomaly.Client.Auth;
using Anomaly.Client.Diagnostics;
using Anomaly.Client.Internal.Bootstrap;
using Il2Cpp;
using Il2CppCentralAuth;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(Anomaly.Client.Core), "Anomaly", "0.1.0", "AnomalyLabs", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: AssemblyMetadata("TargetGameVersion", "14.2.6")]

namespace Anomaly.Client;

public class Core : MelonMod
{
    public static Core Instance { get; private set; }

    public override void OnInitializeMelon()
    {
        Instance = this;

        // Initialize localization first so subsequent init can use Tr.Get()
        // and third-party mods loading after us can register their own namespaces.
        Tr.Register(new LocalizationService(MelonEnvironment.UserDataDirectory));

        HarmonyInstance.PatchAll();

        CustomNetworkManager.Modded = true;
        CentralAuthManager.NoAuthStartupArg = "-noauth";

        AuthHandler.Init();
        ClientRuntime.Initialize();
        DiagnosticBootstrap.Initialize();

        LoggerInstance.Msg($"Initialized (locale: {Tr.ActiveLocale}).");
    }

    public override void OnUpdate()
    {
        ClientRuntime.Update();
        DiagnosticBootstrap.Heartbeat();
    }

    public override void OnApplicationQuit()
    {
        DiagnosticBootstrap.Shutdown();
    }
}
