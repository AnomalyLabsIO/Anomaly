using Anomaly.Client.Api.Internal.Localization;
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
using Anomaly.Client.Diagnostics.News;
using Anomaly.Client.Internal.Bootstrap;
using Anomaly.Client.Api.Internal.UI;
using Anomaly.Client.Internal.UI;
using Anomaly.Client.Internal.Motion;
using Anomaly.Client.Internal.Poses;
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
using System.Reflection;

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
    private static RequiredClientPatch[] _mandatoryPatchDescriptors;
    private static string _mandatoryPatchOwner;

    private readonly struct RequiredClientPatch
    {
        internal readonly MethodBase Target;
        internal readonly MethodInfo Patch;
        internal readonly HarmonyPatchType Kind;

        internal RequiredClientPatch(MethodBase target, MethodInfo patch, HarmonyPatchType kind)
        {
            Target = target;
            Patch = patch;
            Kind = kind;
        }
    }

    internal static bool RequiredConnectionFeaturesReady =>
        Instance != null && Instance._offlineAuthReady && Instance._mandatoryPatchesReady &&
        Instance._clientRuntimeReady &&
        Instance._capabilitiesReady && ClientRuntime.IsInitialized;

    // Game-owned static fields can change without a Harmony or MonoMod event.
    internal static void EnsureOfflineAuthenticationMode()
    {
        bool intact;
        try
        {
            intact = Instance?._offlineAuthReady == true && CustomNetworkManager.Modded &&
                string.Equals(CentralAuthManager.NoAuthStartupArg, "-noauth", StringComparison.Ordinal);
        }
        catch (Exception exception)
        {
            Bootstrap.RuntimeGuard.Invalidate(BootstrapLocalization.Get(TrKeys.Bootstrap_AuthStateUnreadable, exception.GetType().Name));
            throw new ClientIntegrityException(BootstrapLocalization.Get(TrKeys.Bootstrap_AuthStateUnverified));
        }
        if (intact) return;
        Bootstrap.RuntimeGuard.Invalidate(BootstrapLocalization.Get(TrKeys.Bootstrap_AuthStateChanged));
        throw new ClientIntegrityException(BootstrapLocalization.Get(TrKeys.Bootstrap_AuthStateRestart));
    }

    public override void OnEarlyInitializeMelon()
    {
        bool accepted = false;
        string failure = BootstrapLocalization.Get(TrKeys.Bootstrap_MissingProtection);
        try
        {
            var security = MelonPreferences.CreateCategory("AnomalyBootstrap", "Anomaly security");
            BlockingFailureDialog.ConfigurePrompt((security.GetEntry<bool>("ShowProtectionFailurePrompt")
                ?? security.CreateEntry("ShowProtectionFailurePrompt", true)).Value);
            accepted = PullUpBootstraps();
        }
        catch (Exception ex)
        {
            failure += "\r\n\r\n" + ex.Message;
            MelonLogger.Error($"Bootstrap verification failed: {ex}");
        }
        finally
        {
            // This copy has no runtime dependency on the plugin we just rejected.
            if (!accepted) BlockingFailureDialog.ShowAndExit(failure);
        }
    }

    public override void OnInitializeMelon()
    {
        Instance = this;

        // Loader callbacks plus the marked launch log recover failures that predate us.
        NewsDiagnosticService.Initialize();

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
        InitializeFeature("player-poses", PoseRuntime.Initialize);
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

        if (_mandatoryPatchDescriptors == null)
        {
            _mandatoryPatchDescriptors = ResolveMandatoryPatches();
            _mandatoryPatchOwner = owner;
        }
        if (!string.Equals(owner, _mandatoryPatchOwner, StringComparison.Ordinal))
            throw new InvalidOperationException("Anomaly's required Harmony owner changed after initialization.");

        foreach (var patch in _mandatoryPatchDescriptors)
        {
            RequireExactPatch(owner, patch);
            Bootstrap.RuntimeGuard.RegisterRequiredClientPatch(patch.Target, patch.Patch, patch.Kind, owner);
        }
        // Required client tuples must be checked before authentication starts.
        // Native support coverage is completed later on Bootstrap's main thread.
        if (!Bootstrap.RuntimeGuard.Verify(requireComplete: false))
            throw new InvalidOperationException("Bootstrap could not verify Anomaly's required Harmony hooks.");
    }

    private static RequiredClientPatch[] ResolveMandatoryPatches()
    {
        var patches = new List<RequiredClientPatch>();
        AddRequiredPatch(patches, typeof(AuthenticatedFacilitySeedPatch), "Prefix", false,
            typeof(Il2CppMapGeneration.SeedSynchronizer), "ClientReceiveSeed",
            typeof(Il2CppMapGeneration.SeedSynchronizer.SeedMessage));
        AddRequiredPatch(patches, typeof(FacilitySceneReadyPatch), "Postfix", true,
            typeof(Il2CppMapGeneration.SeedSynchronizer), "Awake");
        AddRequiredPatch(patches, typeof(FacilitySceneDestroyedPatch), "Postfix", true,
            typeof(Il2CppMapGeneration.SeedSynchronizer), "OnDestroy");
        AddRequiredPatch(patches, typeof(FacilityGenerateLevelGuardPatch), "Prefix", false,
            typeof(Il2CppMapGeneration.SeedSynchronizer), "GenerateLevel", typeof(bool));
        AddRequiredPatch(patches, typeof(FacilityGenerateLevelGuardPatch), "Postfix", true,
            typeof(Il2CppMapGeneration.SeedSynchronizer), "GenerateLevel", typeof(bool));
        AddRequiredPatch(patches, typeof(AtlasZoneGeneratorPatch), "Prefix", false,
            typeof(Il2CppMapGeneration.AtlasZoneGenerator), "Generate", typeof(Il2CppSystem.Random));
        AddRequiredPatch(patches, typeof(FacilityReadyGatePatch), "Prefix", false,
            typeof(NetworkClient), nameof(NetworkClient.Ready));
        AddRequiredPatch(patches, typeof(FacilityAddPlayerGatePatch), "Prefix", false,
            typeof(NetworkClient), nameof(NetworkClient.AddPlayer));

        AddRequiredPatch(patches, typeof(NetDataWriterPut), nameof(NetDataWriterPut.OnPrefix1), false,
            typeof(CustomLiteNetLib4MirrorTransport),
            nameof(CustomLiteNetLib4MirrorTransport.GetConnectData), typeof(NetDataWriter));
        AddRequiredPatch(patches, typeof(NetDataWriterPut), nameof(NetDataWriterPut.ConnectionRefused), false,
            typeof(CustomLiteNetLib4MirrorTransport),
            nameof(CustomLiteNetLib4MirrorTransport.OnConncetionRefused), typeof(DisconnectInfo));
        AddRequiredPatch(patches, typeof(PlayerAuthenticationManagerPatch), nameof(PlayerAuthenticationManagerPatch.OnReceivePackedMessage), false,
            typeof(EncryptedChannelManager),
            nameof(EncryptedChannelManager.ClientReceivePackedMessage),
            typeof(EncryptedChannelManager.EncryptedMessageOutside));
        AddRequiredPatch(patches, typeof(PlayerAuthenticationManagerPatch), nameof(PlayerAuthenticationManagerPatch.OnPack), false,
            typeof(EncryptedChannelManager),
            nameof(EncryptedChannelManager.TrySendMessageToServer),
            typeof(string), typeof(EncryptedChannelManager.EncryptedChannel));
        AddRequiredPatch(patches, typeof(Patches.Mirror.NetworkManagerRegisterClientMessages), "OnRegister", true,
            typeof(NetworkManager), nameof(NetworkManager.RegisterClientMessages));
        AddRequiredPatch(patches, typeof(CustomNetworkManagerPatch), nameof(CustomNetworkManagerPatch.OnClientConnectPostfix), true,
            typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientConnect));
        AddRequiredPatch(patches, typeof(CustomNetworkManagerPatch), nameof(CustomNetworkManagerPatch.OnClientDisconnectPostfix), true,
            typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientDisconnect));
        AddRequiredPatch(patches, typeof(NetDataWriterPut), nameof(NetDataWriterPut.OnClientDisconnect), false,
            typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientDisconnect));
        AddRequiredPatch(patches, typeof(CustomNetworkManagerPatch), nameof(CustomNetworkManagerPatch.OnClientSceneChangedPostfix), true,
            typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientSceneChanged));
        AddRequiredPatch(patches, typeof(ItemTranslationReaderPatch), "Prefix", false,
            typeof(ItemTranslationReader), nameof(ItemTranslationReader.GetTranslation),
            typeof(ItemType));
        AddRequiredPatch(patches, typeof(HintDisplayPatch), nameof(HintDisplayPatch.ShowLocalPrefix), false,
            typeof(HintDisplay), nameof(HintDisplay.ShowLocal), typeof(HintMessage));
        AddRequiredPatch(patches, typeof(ClientConnectAdmissionGuard), "Prefix", false,
            typeof(LiteNetLib4MirrorClient), nameof(LiteNetLib4MirrorClient.ConnectClient), typeof(NetDataWriter));
        var admissionPrefix = AccessTools.DeclaredMethod(typeof(AdmissionTransportGuard), "Prefix");
        if (admissionPrefix == null)
            throw new MissingMethodException(typeof(AdmissionTransportGuard).FullName, "Prefix");
        foreach (var target in AdmissionTransportGuard.TargetMethods())
            patches.Add(new RequiredClientPatch(target, admissionPrefix, HarmonyPatchType.Prefix));
        return patches.ToArray();
    }

    private static void AddRequiredPatch(List<RequiredClientPatch> patches, Type patchType, string patchMethod, bool postfix,
        Type targetType, string methodName, params Type[] arguments)
    {
        var target = AccessTools.Method(targetType, methodName, arguments);
        if (target == null)
            throw new MissingMethodException(targetType.FullName, methodName);

        var expected = AccessTools.DeclaredMethod(patchType, patchMethod);
        if (expected == null)
            throw new MissingMethodException(patchType.FullName, patchMethod);
        patches.Add(new RequiredClientPatch(target, expected,
            postfix ? HarmonyPatchType.Postfix : HarmonyPatchType.Prefix));
    }

    private static void RequireExactPatch(string owner, RequiredClientPatch required)
    {
        HarmonyLib.Patches patchInfo = HarmonyLib.Harmony.GetPatchInfo(required.Target);
        var patches = required.Kind == HarmonyPatchType.Postfix ? patchInfo?.Postfixes : patchInfo?.Prefixes;
        if (patches == null ||
            !patches.Any(patch => patch.owner == owner && patch.PatchMethod == required.Patch))
            throw new InvalidOperationException(
                $"Mandatory Anomaly patch is missing: {required.Target.DeclaringType?.FullName}.{required.Target.Name} -> {required.Patch.Name}.");
    }

    public override void OnUpdate()
    {
        if (Bootstrap.Core.State != Bootstrap.StartupState.Ready || !Bootstrap.RuntimeGuard.HeartbeatFresh)
        {
            BlockingFailureDialog.ShowAndExit(BootstrapLocalization.Get(TrKeys.Bootstrap_HeartbeatStopped));
            return;
        }
        try { EnsureOfflineAuthenticationMode(); }
        catch (ClientIntegrityException exception)
        {
            BlockingFailureDialog.ShowAndExit(exception.Message);
            return;
        }
        AuthHandler.UpdateIntegrityMonitoring();
        PoseRuntime.Update();
        UpdateFeature("asset-bundles", AssetBundleManager.Update, failClosed: true);
        if (_clientRuntimeReady)
            _clientRuntimeReady = UpdateFeature("runtime-hooks", ClientRuntime.Update, failClosed: true);
        if (_diagnosticsReady)
            _diagnosticsReady = UpdateFeature("diagnostics", DiagnosticBootstrap.Heartbeat);
    }

    public override void OnApplicationQuit()
    {
        ShutdownFeature("player-poses", PoseRuntime.Shutdown);
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
        NewsDiagnosticService.Shutdown();
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
