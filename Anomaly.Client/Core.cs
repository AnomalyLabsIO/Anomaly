using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Compatibility;
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
using Anomaly.Client.Patches.Menu.ModSettings;
using Il2Cpp;
using Il2CppCentralAuth;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(Anomaly.Client.Core), "Anomaly", "0.1.0", "AnomalyLabs", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: SupportedGameVersion("14.2.6^")]

namespace Anomaly.Client;

public class Core : MelonMod
{
    public static Core Instance { get; private set; }

    public override void OnInitializeMelon()
    {
        Instance = this;

        // Initialize localization first so subsequent init can use Tr.Get()
        // and third-party mods loading after us can register their own namespaces.
        Tr.Register(new LocalizationService(MelonEnvironment.UserDataDirectory, typeof(Core).Assembly));

        CustomNetworkManager.Modded = true;
        CentralAuthManager.NoAuthStartupArg = "-noauth";

        AuthHandler.Init();
        ClientRuntime.Initialize();
        DiagnosticBootstrap.Initialize();
        UiBootstrap.Init();
        PreloaderSkinBootstrap.Init();
        MainMenuCustomizer.Init();
        ModSettingsTabInstaller.Init();

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
        ModSettingsSaveScheduler.Flush();
        ClientConfig.DisposeAll();
        ClientPersistence.FlushAll();
        DiagnosticBootstrap.Shutdown();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        PreloaderSkinBootstrap.HandleSceneWasLoaded(sceneName);
    }
}
