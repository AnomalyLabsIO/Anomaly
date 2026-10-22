using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Compatibility;
using Anomaly.Client.Api.Config;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Persistence;
using Anomaly.Client.Api.Scheduling;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Auth;
using Anomaly.Client.Assets;
using Anomaly.Client.Commands;
using Anomaly.Client.Diagnostics;
using Anomaly.Client.Internal.Bootstrap;
using Anomaly.Client.Api.Internal.UI;
using Anomaly.Client.Internal.UI;
using Anomaly.Client.Internal.Motion;
using Anomaly.Client.Internal.Modding;
using Anomaly.Client.Patches.Menu;
using Anomaly.Client.Patches.Menu.ModSettings;
using Anomaly.Client.Patches.Auth;
using Anomaly.Client.Patches.Runtime;
using Anomaly.Client.Patches.Network;
using Anomaly.Client.Patches.Content;
using HarmonyLib;
using Il2Cpp;
using Il2CppCentralAuth;
using Il2CppHints;
using Il2CppInventorySystem.Items;
using Il2CppLiteNetLib;
using Il2CppLiteNetLib.Utils;
using Il2CppMirror;
using Il2CppMirror.LiteNetLib4Mirror;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(Anomaly.Client.Core), "Anomaly", "1.0.0", "AnomalyLabs", null)]
[assembly: MelonID("anomaly.client")]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: SupportedGameVersion("14.2.7^")]
[assembly: PatchShield]

namespace Anomaly.Client;

public class Core : MelonMod
{
    public static Core Instance { get; private set; }
    private bool _clientRuntimeReady;
    private bool _diagnosticsReady;
    private bool _preloaderSkinReady;
    private bool _managedModsReady;
    private bool _mandatoryPatchesReady;
    private bool _capabilitiesReady;
    private bool _offlineAuthReady;

    internal static bool RequiredConnectionFeaturesReady =>
        Instance != null && Instance._offlineAuthReady && Instance._mandatoryPatchesReady &&
        Instance._clientRuntimeReady &&
        Instance._capabilitiesReady && ClientRuntime.IsInitialized;

    public override void OnEarlyInitializeMelon()
    {
        try
        {
            if (PullUpBootstraps())
                return;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Bootstrap verification failed: {ex}");
        }

        Environment.Exit(78);
    }

    public override void OnInitializeMelon()
    {
        Instance = this;

        // Initialize localization first so subsequent init can use Tr.Get()
        // and third-party mods loading after us can register their own namespaces.
        InitializeFeature("localization", () =>
            Tr.Register(new LocalizationService(MelonEnvironment.UserDataDirectory, typeof(Core).Assembly)));

        _offlineAuthReady = InitializeFeature("offline-auth-mode", () =>
        {
            CustomNetworkManager.Modded = true;
            CentralAuthManager.NoAuthStartupArg = "-noauth";
        });

        _mandatoryPatchesReady = InitializeFeature("mandatory-patches", VerifyMandatoryPatches);
        InitializeFeature("authentication", AuthHandler.Init);
        _capabilitiesReady = InitializeFeature("capabilities", ClientCapabilities.Initialize);
        InitializeFeature("motion", LocalPlayerMotionAdapter.Initialize);
        _clientRuntimeReady = InitializeFeature("runtime-hooks", ClientRuntime.Initialize);
        _diagnosticsReady = InitializeFeature("diagnostics", DiagnosticBootstrap.Initialize);
        InitializeFeature("ui", UiBootstrap.Init);
        InitializeFeature("asset-bundles", AssetBundleManager.Initialize);
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
            CommandRegistry.RegisterBuiltIn(new NameCommand());
            CommandRegistry.RegisterBuiltIn(new IdentityCommand());
            CommandRegistry.RegisterBuiltIn(new WhoAmICommand());
            CommandRegistry.RegisterBuiltIn(new StatusCommand());
        });

        LoggerInstance.Msg($"Initialized (locale: {Tr.ActiveLocale}).");
    }

    internal static void VerifyMandatoryPatches()
    {
        string owner = Instance?.HarmonyInstance?.Id;
        if (string.IsNullOrWhiteSpace(owner))
            throw new InvalidOperationException("MelonLoader did not create Anomaly's Harmony owner.");

        RequireOwnedPatch(owner, typeof(AuthenticatedFacilitySeedPatch), "Prefix", false,
            typeof(Il2CppMapGeneration.SeedSynchronizer), "ClientReceiveSeed",
            typeof(Il2CppMapGeneration.SeedSynchronizer.SeedMessage));
        RequireOwnedPatch(owner, typeof(FacilitySceneReadyPatch), "Postfix", true,
            typeof(Il2CppMapGeneration.SeedSynchronizer), "Awake");
        RequireOwnedPatch(owner, typeof(FacilitySceneDestroyedPatch), "Postfix", true,
            typeof(Il2CppMapGeneration.SeedSynchronizer), "OnDestroy");
        RequireOwnedPatch(owner, typeof(FacilityGenerateLevelGuardPatch), "Prefix", false,
            typeof(Il2CppMapGeneration.SeedSynchronizer), "GenerateLevel", typeof(bool));
        RequireOwnedPatch(owner, typeof(FacilityGenerateLevelGuardPatch), "Postfix", true,
            typeof(Il2CppMapGeneration.SeedSynchronizer), "GenerateLevel", typeof(bool));
        RequireOwnedPatch(owner, typeof(AtlasZoneGeneratorPatch), "Prefix", false,
            typeof(Il2CppMapGeneration.AtlasZoneGenerator), "Generate", typeof(Il2CppSystem.Random));
        RequireOwnedPatch(owner, typeof(FacilityReadyGatePatch), "Prefix", false,
            typeof(NetworkClient), nameof(NetworkClient.Ready));
        RequireOwnedPatch(owner, typeof(FacilityAddPlayerGatePatch), "Prefix", false,
            typeof(NetworkClient), nameof(NetworkClient.AddPlayer));

        RequireOwnedPatch(owner, typeof(NetDataWriterPut), nameof(NetDataWriterPut.OnPrefix1), false,
            typeof(CustomLiteNetLib4MirrorTransport),
            nameof(CustomLiteNetLib4MirrorTransport.GetConnectData), typeof(NetDataWriter));
        RequireOwnedPatch(owner, typeof(NetDataWriterPut), nameof(NetDataWriterPut.ConnectionRefused), false,
            typeof(CustomLiteNetLib4MirrorTransport),
            nameof(CustomLiteNetLib4MirrorTransport.OnConncetionRefused), typeof(DisconnectInfo));
        RequireOwnedPatch(owner, typeof(PlayerAuthenticationManagerPatch), nameof(PlayerAuthenticationManagerPatch.OnReceivePackedMessage), false,
            typeof(EncryptedChannelManager),
            nameof(EncryptedChannelManager.ClientReceivePackedMessage),
            typeof(EncryptedChannelManager.EncryptedMessageOutside));
        RequireOwnedPatch(owner, typeof(PlayerAuthenticationManagerPatch), nameof(PlayerAuthenticationManagerPatch.OnPack), false,
            typeof(EncryptedChannelManager),
            nameof(EncryptedChannelManager.TrySendMessageToServer),
            typeof(string), typeof(EncryptedChannelManager.EncryptedChannel));
        RequireOwnedPatch(owner, typeof(Patches.Mirror.NetworkManagerRegisterClientMessages), "OnRegister", true,
            typeof(NetworkManager), nameof(NetworkManager.RegisterClientMessages));
        RequireOwnedPatch(owner, typeof(CustomNetworkManagerPatch), nameof(CustomNetworkManagerPatch.OnClientConnectPostfix), true,
            typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientConnect));
        RequireOwnedPatch(owner, typeof(CustomNetworkManagerPatch), nameof(CustomNetworkManagerPatch.OnClientDisconnectPostfix), true,
            typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientDisconnect));
        RequireOwnedPatch(owner, typeof(NetDataWriterPut), nameof(NetDataWriterPut.OnClientDisconnect), false,
            typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientDisconnect));
        RequireOwnedPatch(owner, typeof(CustomNetworkManagerPatch), nameof(CustomNetworkManagerPatch.OnClientSceneChangedPostfix), true,
            typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientSceneChanged));
        RequireOwnedPatch(owner, typeof(ItemTranslationReaderPatch), "Prefix", false,
            typeof(ItemTranslationReader), nameof(ItemTranslationReader.GetTranslation),
            typeof(ItemType));
        RequireOwnedPatch(owner, typeof(HintDisplayPatch), nameof(HintDisplayPatch.ShowLocalPrefix), false,
            typeof(HintDisplay), nameof(HintDisplay.ShowLocal), typeof(HintMessage));
        RequireOwnedPatch(owner, typeof(ClientConnectAdmissionGuard), "Prefix", false,
            typeof(LiteNetLib4MirrorClient), nameof(LiteNetLib4MirrorClient.ConnectClient), typeof(NetDataWriter));
        foreach (var target in AdmissionTransportGuard.TargetMethods())
            RequireExactPatch(owner, target, AccessTools.DeclaredMethod(typeof(AdmissionTransportGuard), "Prefix"), false);
    }

    private static void RequireOwnedPatch(string owner, Type patchType, string patchMethod, bool postfix,
        Type targetType, string methodName, params Type[] arguments)
    {
        var target = AccessTools.Method(targetType, methodName, arguments);
        if (target == null)
            throw new MissingMethodException(targetType.FullName, methodName);

        RequireExactPatch(owner, target, AccessTools.DeclaredMethod(patchType, patchMethod), postfix);
    }

    private static void RequireExactPatch(string owner, System.Reflection.MethodBase target,
        System.Reflection.MethodInfo expected, bool postfix)
    {
        HarmonyLib.Patches patchInfo = HarmonyLib.Harmony.GetPatchInfo(target);
        var patches = postfix ? patchInfo?.Postfixes : patchInfo?.Prefixes;
        if (expected == null || patches == null ||
            !patches.Any(patch => patch.owner == owner && patch.PatchMethod == expected))
            throw new InvalidOperationException(
                $"Mandatory Anomaly patch is missing: {target.DeclaringType?.FullName}.{target.Name} -> {expected?.Name}.");
    }

    public override void OnUpdate()
    {
        AuthHandler.UpdateIntegrityMonitoring();
        UpdateFeature("asset-bundles", AssetBundleManager.Update, failClosed: true);
        if (_clientRuntimeReady)
            _clientRuntimeReady = UpdateFeature("runtime-hooks", ClientRuntime.Update, failClosed: true);
        if (_diagnosticsReady)
            _diagnosticsReady = UpdateFeature("diagnostics", DiagnosticBootstrap.Heartbeat);
    }

    public override void OnApplicationQuit()
    {
        if (_managedModsReady) ShutdownFeature("managed-mods", ManagedModRuntime.Shutdown);
        if (_clientRuntimeReady) ShutdownFeature("runtime-hooks", ClientRuntime.Shutdown);
        RuntimeTeardown.FlushBeforeCancel(
            () =>
            {
                ModSettingsSaveScheduler.Flush();
                ClientConfig.DisposeAll();
                ClientPersistence.FlushAll();
            },
            Scheduler.ClearAll);
        ClientCapabilities.Shutdown();
        ClientVisibility.ReleaseAll();
        LocalPlayerMotionAdapter.Shutdown();
        ShutdownFeature("asset-bundles", () =>
        {
            Anomaly.Client.Content.ContentCatalogClient.ResetForShutdown();
            PreloaderSkinBootstrap.RestoreAll();
            MainMenuCustomizer.RestoreLogo();
            AssetBundleManager.Shutdown();
        });
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

    private static bool UpdateFeature(string featureId, Action update, bool failClosed = false)
    {
        try
        {
            update();
            return true;
        }
        catch (Exception ex)
        {
            if (failClosed)
                AnomalyMessaging.FailClosedTransport($"Required feature '{featureId}' failed at runtime.");
            try
            {
                FeatureHealth.Report("anomaly", featureId, FeatureHealthStatus.Failed,
                    "Runtime failure: " + ex.GetType().Name);
                MelonLogger.Error($"Feature '{featureId}' failed at runtime and was disabled: {ex}");
            }
            catch { }
            return false;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool PullUpBootstraps()
    {
        if (Bootstrap.Core.State != Bootstrap.StartupState.Ready ||
            !Bootstrap.RuntimeGuard.Verify(requireComplete: false))
        {
            MelonLogger.Error("Anomaly bootstrap failed to initialize. Aborting.");
            return false;
        }
        var bootstrap = MelonBase.RegisteredMelons.OfType<Bootstrap.Core>().SingleOrDefault();
        if (bootstrap != null && ReferenceEquals(bootstrap.GetType().Assembly, typeof(Bootstrap.Core).Assembly) &&
            bootstrap.MelonAssembly.Assembly.IsDefined(typeof(PatchShield), inherit: false))
        {
            MelonLogger.Msg("Anomaly Bootstrap is loaded and its required guard is active.");
            return true;
        }
        MelonLogger.Error("Anomaly bootstrap was not loaded. Aborting.");
        return false;
    }
}
