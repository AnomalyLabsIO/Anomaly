using System.Reflection;
using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Config;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Persistence;
using Anomaly.Client.Auth;
using Anomaly.Client.Commands;
using Anomaly.Client.Diagnostics;
using Anomaly.Client.Internal.Bootstrap;
using Anomaly.Client.Api.Internal.UI;
using Anomaly.Client.Internal.UI;
using Anomaly.Client.Patches.Menu;
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
    // Mirror of Anomaly.InstallerCore.InstallerService.FirstRunArg. Duplicated rather than
    // shared via Anomaly.Shared because Shared targets net4.8 and InstallerCore targets net8.0.
    // The installer passes this on its final step to make MelonLoader generate the Il2Cpp
    // managed assemblies; we exit before any real init so the install completes cleanly.
    // Never passed by the user during normal play — installer-only.
    private const string FirstRunArg = "--anomaly-firstrun";

    public static Core Instance { get; private set; }

    public override void OnInitializeMelon()
    {
        Instance = this;

        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, FirstRunArg, StringComparison.Ordinal))
            {
                LoggerInstance.Msg("First-run arg detected; managed assemblies are now generated. Exiting.");
                Environment.Exit(0);
                return;
            }
        }

        // Initialize localization first so subsequent init can use Tr.Get()
        // and third-party mods loading after us can register their own namespaces.
        Tr.Register(new LocalizationService(MelonEnvironment.UserDataDirectory, typeof(Core).Assembly));

        HarmonyInstance.PatchAll();

        CustomNetworkManager.Modded = true;
        CentralAuthManager.NoAuthStartupArg = "-noauth";

        AuthHandler.Init();
        ClientRuntime.Initialize();
        DiagnosticBootstrap.Initialize();
        UiBootstrap.Init();
        PreloaderSkinBootstrap.Init();
        MainMenuCustomizer.Init();

        CommandRegistry.Register("anomaly", new NameCommand());

        LoggerInstance.Msg($"Initialized (locale: {Tr.ActiveLocale}).");
    }

    public override void OnUpdate()
    {
        ClientRuntime.Update();
        DiagnosticBootstrap.Heartbeat();
    }

    public override void OnApplicationQuit()
    {
        ClientConfig.DisposeAll();
        ClientPersistence.FlushAll();
        DiagnosticBootstrap.Shutdown();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        PreloaderSkinBootstrap.HandleSceneWasLoaded(sceneName);
    }
}
