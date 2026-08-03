using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Compatibility;
using Anomaly.Client.Api.Config;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Persistence;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Auth;
using Anomaly.Client.Commands;
using Anomaly.Client.Diagnostics;
using Anomaly.Client.Internal.Bootstrap;
using Anomaly.Client.Api.Internal.UI;
using Anomaly.Client.Internal.UI;
using Anomaly.Client.Internal.Motion;
using Anomaly.Client.Internal.Modding;
using Anomaly.Client.Patches.Menu;
using Anomaly.Client.Patches.Menu.ModSettings;
using Il2Cpp;
using Il2CppCentralAuth;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(Anomaly.Client.Core), "Anomaly", "0.1.0", "AnomalyLabs", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: SupportedGameVersion("14.2.7^")]

namespace Anomaly.Client;

public class Core : MelonMod
{
    public static Core Instance { get; private set; }
    private bool _clientRuntimeReady;
    private bool _diagnosticsReady;
    private bool _preloaderSkinReady;
    private bool _managedModsReady;

    public override void OnInitializeMelon()
    {
        Instance = this;

        // Initialize localization first so subsequent init can use Tr.Get()
        // and third-party mods loading after us can register their own namespaces.
        InitializeFeature("localization", () =>
            Tr.Register(new LocalizationService(MelonEnvironment.UserDataDirectory, typeof(Core).Assembly)));

        InitializeFeature("offline-auth-mode", () =>
        {
            CustomNetworkManager.Modded = true;
            CentralAuthManager.NoAuthStartupArg = "-noauth";
        });

        InitializeFeature("authentication", AuthHandler.Init);
        InitializeFeature("capabilities", ClientCapabilities.Initialize);
        InitializeFeature("motion", LocalPlayerMotionAdapter.Initialize);
        _clientRuntimeReady = InitializeFeature("runtime-hooks", ClientRuntime.Initialize);
        _diagnosticsReady = InitializeFeature("diagnostics", DiagnosticBootstrap.Initialize);
        InitializeFeature("ui", UiBootstrap.Init);
        _preloaderSkinReady = InitializeFeature("preloader-skin", PreloaderSkinBootstrap.Init);
        InitializeFeature("main-menu", MainMenuCustomizer.Init);
        InitializeFeature("mod-settings", ModSettingsTabInstaller.Init);
        // ManagedModRuntime reports Healthy/Disabled/Failed with graph-specific detail. Do
        // not overwrite a safe-mode or graph failure report merely because it failed soft.
        _managedModsReady = InitializeFeature(
            "managed-mods",
            ManagedModRuntime.Initialize,
            reportHealthyOnReturn: false);

        InitializeFeature("commands", () =>
        {
            CommandRegistry.Register("anomaly", new NameCommand());
            CommandRegistry.Register("anomaly", new IdentityCommand());
        });

        LoggerInstance.Msg($"Initialized (locale: {Tr.ActiveLocale}).");
    }

    public override void OnUpdate()
    {
        if (_clientRuntimeReady)
            _clientRuntimeReady = UpdateFeature("runtime-hooks", ClientRuntime.Update);
        if (_diagnosticsReady)
            _diagnosticsReady = UpdateFeature("diagnostics", DiagnosticBootstrap.Heartbeat);
    }

    public override void OnApplicationQuit()
    {
        if (_managedModsReady) ShutdownFeature("managed-mods", ManagedModRuntime.Shutdown);
        ModSettingsSaveScheduler.Flush();
        ClientConfig.DisposeAll();
        ClientPersistence.FlushAll();
        ClientCapabilities.Shutdown();
        ClientVisibility.ReleaseAll();
        LocalPlayerMotionAdapter.Shutdown();
        if (_diagnosticsReady) ShutdownFeature("diagnostics", DiagnosticBootstrap.Shutdown);
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (_preloaderSkinReady) PreloaderSkinBootstrap.HandleSceneWasLoaded(sceneName);
    }

    private static bool InitializeFeature(
        string featureId,
        Action initialize,
        bool reportHealthyOnReturn = true)
    {
        try
        {
            initialize();
            if (reportHealthyOnReturn)
                FeatureHealth.Report("anomaly", featureId, FeatureHealthStatus.Healthy, string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            FeatureHealth.Report("anomaly", featureId, FeatureHealthStatus.Failed,
                ex.GetType().Name + ": " + ex.Message);
            MelonLogger.Error($"Feature '{featureId}' failed to initialize and was disabled: {ex}");
            return false;
        }
    }

    private static void ShutdownFeature(string featureId, Action shutdown)
    {
        try { shutdown(); }
        catch (Exception ex) { MelonLogger.Error($"Feature '{featureId}' failed during shutdown: {ex}"); }
    }

    private static bool UpdateFeature(string featureId, Action update)
    {
        try
        {
            update();
            return true;
        }
        catch (Exception ex)
        {
            FeatureHealth.Report("anomaly", featureId, FeatureHealthStatus.Failed,
                "Runtime failure: " + ex.GetType().Name);
            MelonLogger.Error($"Feature '{featureId}' failed at runtime and was disabled: {ex}");
            return false;
        }
    }
}
