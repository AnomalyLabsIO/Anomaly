using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Assets;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.FileTransfer;
using Anomaly.Client.Content;
using Anomaly.Client.Internal.Audio;
using Anomaly.Client.Internal.Map;
using Anomaly.Client.Internal.Networking;
using Anomaly.Client.Internal.PlayerCustomization;
using Anomaly.Client.Internal.Round;
using Anomaly.Client.Patches.Audio;
using Anomaly.Client.Patches.Auth;
using Anomaly.Shared.Networking;
using HarmonyLib;
using Il2CppMirror;
using MelonLoader;

namespace Anomaly.Client.Patches.Mirror;

[HarmonyPatch(typeof(NetworkManager),nameof(NetworkManager.RegisterClientMessages))]
public static class NetworkManagerRegisterClientMessages
{
    private static bool _cleanupInstalled;
    private static bool _registryReady;
    private static bool _silentRoleReady;
    private static bool _playerColorReady;
    private static bool _fileCacheReady;
    private static bool _fileTransferReady;
    private static bool _contentReady;
    private static bool _enrollmentReady;
    private static bool _overrideReady;
    private static bool _visualReady;
    private static bool _pathOverrideReady;
    private static bool _announcerReady;
    private static bool _roundReady;

    internal static bool IsReady { get; private set; }

    [HarmonyPostfix]
    public static void OnRegister()
    {
        IsReady = false;
        try
        {
            InstallDisconnectCleanup();
            bool ready = true;
            ready &= EncryptedChannelHandlerFix.EnsureRegistered();
            ready &= TryInitialize("message-registry", ref _registryReady, () =>
            {
                AnomalyMessageRegistry.Log = s => MelonLogger.Msg(s);
                AnomalyMessageRegistry.Warn = s => MelonLogger.Warning(s);
                AnomalyMessageRegistry.RegisterDefaults();
            });
            ready &= TryInitialize("silent-role", ref _silentRoleReady, SilentRoleChangeHandler.Register);
            ready &= TryInitialize("player-color", ref _playerColorReady, PlayerColorTracker.Init);
            ready &= TryInitialize("file-cache", ref _fileCacheReady, FileCache.Init);
            ready &= TryInitialize("file-transfer", ref _fileTransferReady, FileTransferClient.Init);
            ready &= TryInitialize("content", ref _contentReady, ContentCatalogClient.Init);
            ready &= TryInitialize("enrollment", ref _enrollmentReady, EnrollmentIdentityReceiver.Init);
            ready &= TryInitialize("asset-overrides", ref _overrideReady, OverrideRegistry.Init);
            ready &= TryInitialize("decorative-visuals", ref _visualReady, DecorativeVisualClient.Init);
            ready &= TryInitialize("path-overrides", ref _pathOverrideReady, PathOverrideRegistry.Init);
            ready &= TryInitialize("announcer", ref _announcerReady, AnnouncerVocabularyReceiver.Init);
            ready &= TryInitialize("round-state", ref _roundReady, RoundStateReceiver.Init);

            // Facility bootstrap uses the internal authenticated message receiver.
            FacilityRecipeClient.RegisterHandlers();

            IsReady = ready;
            FeatureHealth.Report(
                "anomaly",
                "networking",
                ready ? FeatureHealthStatus.Healthy : FeatureHealthStatus.Failed,
                ready
                    ? string.Empty
                    : "One or more required message, encrypted-channel, transfer, content, or lifecycle services failed to initialize. " +
                      (EncryptedChannelHandlerFix.LastError ?? string.Empty));
            if (!ready)
                RollBackConnectionState();
        }
        catch(Exception e)
        {
            IsReady = false;
            RollBackConnectionState();
            FeatureHealth.Report("anomaly", "networking", FeatureHealthStatus.Failed,
                e.GetType().Name + ": " + e.Message);
            MelonLogger.Error("Patch.Il2CppMirror.NetworkManager.RegisterClientMessages: " + e);
        }
    }

    private static bool TryInitialize(string name, ref bool initialized, Action initialize)
    {
        if (initialized)
            return true;
        try
        {
            initialize();
            initialized = true;
            return true;
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"Required networking component '{name}' failed to initialize: {exception}");
            return false;
        }
    }

    private static void InstallDisconnectCleanup()
    {
        if (_cleanupInstalled)
            return;
        try
        {
            ClientEvents.Disconnected += _ =>
            {
                IsReady = false;
                RollBackConnectionState();
            };
            _cleanupInstalled = true;
        }
        catch
        {
            _cleanupInstalled = false;
            throw;
        }
    }

    private static void RollBackConnectionState()
    {
        IsolatedCleanup.Run(
            (component, exception) =>
                MelonLogger.Error($"Connection cleanup failed for '{component}': {exception}"),
            // Revoke authentication and bounded logical state first. Unity object
            // restoration below is best effort and cannot suppress these resets.
            new CleanupStep("encrypted-channel bridge", EncryptedChannelHandlerFix.ResetConnectionState),
            new CleanupStep("transport session", AnomalyMessaging.ClearTransportSession),
            new CleanupStep("content catalog", ContentCatalogClient.Reset),
            new CleanupStep("file transfer authorization", FileTransferClient.Reset),
            new CleanupStep("replay and rate limits", AnomalyReplayTable.Reset),
            new CleanupStep("pending encrypted messages", PlayerAuthenticationManagerPatch.ResetPendingClientMessages),
            new CleanupStep("enrollment identity", () =>
                Anomaly.Client.Api.Features.Client.EnrollmentId = null),
            new CleanupStep("round state", RoundStateReceiver.Reset),
            new CleanupStep("facility recipe", FacilityRecipeClient.Clear),
            new CleanupStep("texture overrides", OverrideRegistry.Clear),
            new CleanupStep("path overrides", () =>
            {
                try
                {
                    if (_pathOverrideReady)
                        PathOverrideRegistry.Shutdown();
                }
                finally
                {
                    _pathOverrideReady = false;
                }
            }),
            new CleanupStep("announcer vocabulary", AnnouncerVocabularyReceiver.Clear),
            new CleanupStep("announcer schedule", CassieLineSourceSchedulePatch.RestoreAll));
    }
}
