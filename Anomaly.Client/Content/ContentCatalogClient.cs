using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Assets;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Auth;
using Anomaly.Client.Components;
using Anomaly.Client.FileTransfer;
using Anomaly.Client.Internal.Audio;
using Anomaly.Client.Internal.Bootstrap;
using Anomaly.Client.Patches.Audio;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Il2CppMirror;
using MelonLoader;

namespace Anomaly.Client.Content;

/// <summary>Authenticated offer -> consent -> transfer -> transactional activation coordinator.</summary>
internal static class ContentCatalogClient
{
    private const string PromptOwner = "content-catalog";
    private const string ScriptPromptOwner = "script-execution";
    private static readonly TransactionalInitializer Initializer = new();
    private static ContentCatalog _catalog;
    private static FileManifestMessage _manifest;
    private static bool _decisionSent;
    private static bool _declined;
    private static bool _failureLatched;
    private static bool _activationSent;
    private static bool _activationPending;
    private static bool _qualificationPending;
    private static bool _offerResolutionStarted;
    private static string _offerFingerprint;
    private static long _offerGeneration;
    internal static bool IsActivated { get; private set; }

    internal static void Init()
    {
        Initializer.Initialize(
            new InitializationStep(ConnectionContentBootstrap.Initialize, ConnectionContentBootstrap.Shutdown),
            new InitializationStep(ContentConsentStore.Init, () => { }),
            new InitializationStep(
                () => AnomalyMessaging.MessageReceived += HandleMessage,
                () => AnomalyMessaging.MessageReceived -= HandleMessage),
            new InitializationStep(
                () => FileTransferClient.ManifestOffered += HandleManifest,
                () => FileTransferClient.ManifestOffered -= HandleManifest),
            new InitializationStep(
                () => FileTransferClient.TransferFinished += HandleTransferFinished,
                () => FileTransferClient.TransferFinished -= HandleTransferFinished),
            new InitializationStep(
                () => ClientEvents.Disconnected += HandleDisconnected,
                () => ClientEvents.Disconnected -= HandleDisconnected));
    }

    private static void HandleDisconnected(Anomaly.Client.Api.Events.Args.ClientDisconnectedEventArgs _) => Reset();

    /// <summary>Starts the replacement player hub's content offer without replacing its transport session.</summary>
    internal static void BeginFacilityScene()
    {
        if (ConnectionContentBootstrap.IsAcquiring) return;
        FileTransferClient.Reset();
        Reset(throwOnCleanupFailure: true);
        OverrideRegistry.Clear();
        AnnouncerVocabularyReceiver.Clear();
    }

    internal static void Reset() => Reset(throwOnCleanupFailure: false);
    internal static void ResetForShutdown() => Reset(throwOnCleanupFailure: true);

    private static void Reset(bool throwOnCleanupFailure)
    {
        IsActivated = false;
        if (_offerGeneration < long.MaxValue)
            _offerGeneration++;
        _catalog = null;
        _manifest = null;
        _decisionSent = false;
        _declined = false;
        _failureLatched = false;
        _activationSent = false;
        _activationPending = false;
        _qualificationPending = false;
        _offerResolutionStarted = false;
        _offerFingerprint = null;
        Exception cleanupFailure = null;
        try { ScriptingBootstrap.RevokeAll(); }
        catch (Exception exception) { cleanupFailure = exception; }
        ScriptPackManager.Reset();
        bool addressLayerRevoked = false;
        try { AssetBundleManager.RevokeRemote(); addressLayerRevoked = true; }
        catch (Exception exception)
        {
            cleanupFailure = exception;
            SafeLogError("Asset address revocation failed after logical content state was cleared: " + exception);
        }
        try { PasswordPrompt.Hide(PromptOwner); }
        catch (Exception exception)
        {
            cleanupFailure ??= exception;
            SafeLogError("Content prompt cleanup failed after logical state was revoked: " + exception);
        }
        try { PasswordPrompt.Hide(ScriptPromptOwner); }
        catch (Exception exception) { cleanupFailure ??= exception; }
        try { if (addressLayerRevoked) ReleaseAssetBindings(); }
        catch (Exception exception)
        {
            cleanupFailure ??= exception;
            SafeLogError("Content registrar cleanup failed after logical state was revoked: " + exception);
        }
        if (cleanupFailure != null) _failureLatched = true;
        if (throwOnCleanupFailure && cleanupFailure != null)
            throw new InvalidOperationException("Previous scene content could not be completely released.", cleanupFailure);
    }

    private static void HandleMessage(IAnomalyMessage message)
    {
        if (_failureLatched) return;
        if (message is not ContentCatalogMessage catalogMessage)
            return;

        try
        {
            ContentCatalogMessage.ValidateCatalog(catalogMessage.Catalog, validateDeclaredHash: true);
            if (_catalog != null &&
                !CatalogBuilder.HashesEqual(_catalog.CatalogSha256, catalogMessage.Catalog.CatalogSha256))
            {
                Fail("server changed its catalog during the active connection");
                return;
            }
            if (_catalog == null)
            {
                if (_offerGeneration == long.MaxValue)
                    throw new InvalidOperationException("Content offer generation is exhausted.");
                _offerGeneration++;
            }
            _catalog = catalogMessage.Catalog;
            TryResolveOffer();
        }
        catch (Exception exception)
        {
            Fail("catalog validation failed: " + exception.Message);
        }
    }

    internal static void ReceiveBootstrap(IAnomalyMessage message) => HandleMessage(message);

    private static void HandleManifest(FileManifestMessage manifest)
    {
        if (_failureLatched) return;
        try
        {
            _manifest = manifest;
            TryResolveOffer();
        }
        catch (Exception exception)
        {
            Fail("manifest coordination failed: " + exception.Message);
        }
    }

    internal static void Tick()
    {
        if (!_qualificationPending) return;
        if (_catalog == null || _manifest == null || _declined || _failureLatched ||
            !ConnectionContentBootstrap.IsCurrentScriptOffer(_manifest.SessionId, _catalog.CatalogSha256, _catalog.ManifestSha256))
        {
            _qualificationPending = false;
            return;
        }
        if (ScriptingBootstrap.IsRemoteQualificationPending) return;
        _qualificationPending = false;
        TryResolveOffer();
    }

    private static void TryResolveOffer()
    {
        if (_catalog == null || _manifest == null || _decisionSent || _declined || _failureLatched || _offerResolutionStarted)
            return;
        if (string.IsNullOrWhiteSpace(AuthHandler.CurrentServerFingerprint))
        {
            Fail("the authenticated server fingerprint is unavailable");
            return;
        }
        if (_offerFingerprint != null && !string.Equals(_offerFingerprint, AuthHandler.CurrentServerFingerprint, StringComparison.Ordinal))
        {
            Fail("the authenticated server changed while worker qualification was pending");
            return;
        }
        if (!CatalogBuilder.HashesEqual(_catalog.ManifestSha256, _manifest.ManifestSha256))
        {
            Fail("catalog and file manifest digests did not match");
            return;
        }

        try
        {
            ValidateCatalogFiles(_catalog, _manifest);
            _offerFingerprint ??= AuthHandler.CurrentServerFingerprint;
            ConnectionContentBootstrap.BindOffer(_manifest.SessionId, _catalog.CatalogSha256, _catalog.ManifestSha256);
            if (_catalog.ScriptPacks.Any(pack => pack.Sources.Count != 0) && !ScriptingPolicy.RemoteExecutionEnabled)
            {
                if (ScriptingBootstrap.IsRemoteQualificationPending)
                {
                    _qualificationPending = true;
                    return;
                }
                throw new NotSupportedException(ScriptingPolicy.RemoteExecutionDisabledReason);
            }
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
            return;
        }

        _qualificationPending = false;
        _offerResolutionStarted = true;
        if (ContentConsentStore.IsApproved(
                AuthHandler.CurrentServerFingerprint,
                _catalog.CatalogSha256,
                _catalog.ManifestSha256))
        {
            Approve();
            return;
        }

        long totalBytes = _manifest.Files.Sum(file => (long)file.Size);
        string fingerprint = AuthHandler.CurrentServerFingerprint;
        byte[] catalogHash = (byte[])_catalog.CatalogSha256.Clone();
        byte[] manifestHash = (byte[])_catalog.ManifestSha256.Clone();
        long offerGeneration = _offerGeneration;
        ulong activationId = _manifest.SessionId;
        long transportGeneration = AnomalyMessaging.TransportGeneration;
        var pointer = NetworkClient.connection?.Pointer ?? IntPtr.Zero;
        bool CurrentConsent() => offerGeneration == _offerGeneration && !_decisionSent && !_declined && !_failureLatched &&
            AnomalyMessaging.TransportGeneration == transportGeneration && _catalog != null && _manifest?.SessionId == activationId &&
            CatalogBuilder.HashesEqual(catalogHash, _catalog.CatalogSha256) &&
            CatalogBuilder.HashesEqual(manifestHash, _catalog.ManifestSha256) &&
            string.Equals(fingerprint, AuthHandler.CurrentServerFingerprint, StringComparison.Ordinal) &&
            (NetworkClient.connection == null || (NetworkClient.connection.Pointer == pointer &&
                ConnectionContentBootstrap.IsCurrentScriptOffer(activationId, catalogHash, manifestHash)));
        PasswordPrompt.ShowConfirmation(
            PromptOwner,
            Tr.Get(TrKeys.Content_ApprovalTitle),
            Tr.Get(TrKeys.Content_ApprovalDetails, ServerTrustStore.FormatFingerprint(fingerprint),
                _catalog.Entries.Count, _manifest.Files.Count, totalBytes / 1048576d, totalBytes),
            Tr.Get(TrKeys.Prompt_AllowContent),
            Tr.Get(TrKeys.Prompt_SkipContent),
            () =>
            {
                if (!CurrentConsent())
                    return;
                if (!ContentConsentStore.TryRemember(fingerprint, catalogHash, manifestHash))
                {
                    Fail("consent could not be persisted safely");
                    return;
                }
                Approve();
            },
            () =>
            {
                if (CurrentConsent())
                    DeclineByUser("user declined the content offer");
            });
    }

    private static void Approve()
    {
        if (_decisionSent || _catalog == null)
            return;
        _decisionSent = true;
        ConnectionContentBootstrap.Consent(_manifest.SessionId, _catalog.CatalogSha256, _catalog.ManifestSha256);
        if (!AnomalyMessaging.TrySend(new ContentCatalogDecisionMessage
        {
            CatalogSha256 = (byte[])_catalog.CatalogSha256.Clone(),
            ManifestSha256 = (byte[])_catalog.ManifestSha256.Clone(),
            Decision = ContentCatalogDecision.Approved,
        }))
        {
            Fail("the exact content approval could not be delivered over the authenticated transport");
            return;
        }
        if (NetworkClient.connection == null)
        {
            FailActivation("the approved content connection disappeared before transfer",
                _catalog.Packs.Select(pack => pack.PackId).Concat(_catalog.ScriptPacks.Select(pack => pack.PackId))
                    .Concat(_catalog.Entries.Select(entry => entry.Key)));
            return;
        }
        ReportHealth(FeatureHealthStatus.Degraded,
            "The exact authenticated catalog and manifest were approved; activation is pending.");
        if (!FileTransferClient.ApprovePendingManifest(_catalog.ManifestSha256))
            FailActivation("the approved manifest could not enter the transfer state",
                _catalog.Entries.Select(entry => entry.Key));
    }

    private static void DeclineByUser(string reason)
    {
        if (_declined)
            return;
        _declined = true;
        bool delivered = _decisionSent && _activationPending
            ? SendActivation(false, Array.Empty<string>()) : SendDecline();
        DisconnectAfterFailedAcknowledgement("Required server assets declined: " + reason, delivered);
        ReportHealth(FeatureHealthStatus.Disabled, reason);
        SafeLogWarning("Custom content declined: " + reason);
    }

    private static void Fail(string reason)
    {
        if (_failureLatched)
            return;
        _failureLatched = true;
        _declined = true;
        try { PasswordPrompt.Hide(PromptOwner); }
        catch (Exception exception) { SafeLogError("Content prompt cleanup failed: " + exception); }
        RevokeMaterializedContent();
        try { SendDecline(); }
        catch { }
        AnomalyMessaging.FailClosedTransport("Required custom-content validation failed: " + reason);
        ReportHealth(FeatureHealthStatus.Failed, reason);
        SafeLogError("Required custom-content validation failed: " + reason);
        ShowContentFailure(Tr.Get(TrKeys.Content_ValidationFailed), "CONTENT-VALIDATION");
    }

    private static bool SendDecline()
    {
        bool delivered = _catalog == null;
        try
        {
            if (_catalog != null)
            {
                delivered = AnomalyMessaging.TrySend(new ContentCatalogDecisionMessage
                {
                    CatalogSha256 = (byte[])_catalog.CatalogSha256.Clone(),
                    ManifestSha256 = (byte[])_catalog.ManifestSha256.Clone(),
                    Decision = ContentCatalogDecision.Declined,
                });
            }
        }
        catch (Exception exception)
        {
            SafeLogWarning("Could not send the fail-closed content decline: " + exception.Message);
        }
        try
        {
            if (_manifest != null)
                FileTransferClient.DeclinePendingManifest(_manifest.ManifestSha256);
        }
        catch (Exception exception)
        {
            SafeLogWarning("Could not clear the pending content manifest: " + exception.Message);
        }
        return delivered;
    }

    private static void HandleTransferFinished(bool successful)
    {
        if (_declined || _failureLatched || !_decisionSent || _activationSent || _activationPending || _catalog == null || _manifest == null)
            return;
        _activationPending = true;
        string[] catalogKeys = _catalog.Packs.Select(pack => pack.PackId).Concat(_catalog.ScriptPacks.Select(pack => pack.PackId))
            .Concat(_catalog.Entries.Select(entry => entry.Key)).Distinct(StringComparer.Ordinal).Take(128).ToArray();
        try
        {
            if (!successful)
            {
                FailActivation("the approved file transfer failed", catalogKeys);
                return;
            }

            long generation = _offerGeneration;
            long transportGeneration = AnomalyMessaging.TransportGeneration;
            var pointer = NetworkClient.connection?.Pointer ?? IntPtr.Zero;
            var fingerprint = AuthHandler.CurrentServerFingerprint;
            ulong activationId = _manifest.SessionId;
            ContentCatalog catalog = _catalog;
            var catalogHash = (byte[])catalog.CatalogSha256.Clone();
            var manifestHash = (byte[])catalog.ManifestSha256.Clone();
            bool OwnsActivation() => generation == _offerGeneration && !_declined && !_failureLatched &&
                _catalog != null && _manifest?.SessionId == activationId &&
                CatalogBuilder.HashesEqual(catalogHash, _catalog.CatalogSha256) &&
                CatalogBuilder.HashesEqual(manifestHash, _catalog.ManifestSha256) &&
                string.Equals(fingerprint, AuthHandler.CurrentServerFingerprint, StringComparison.Ordinal) &&
                AnomalyMessaging.TransportGeneration == transportGeneration;
            bool CheckCurrent()
            {
                if (!OwnsActivation()) return false;
                // A missing native connection is a failure of this still-owned
                // transaction, not evidence that a replacement owns its resources.
                if (NetworkClient.connection == null)
                {
                    FailActivation("the approved content connection disappeared during initialization", catalogKeys);
                    return false;
                }
                return NetworkClient.connection.Pointer == pointer &&
                    ConnectionContentBootstrap.IsCurrentScriptOffer(activationId, catalogHash, manifestHash);
            }
            bool bundleCompleted = false;
            AssetBundleManager.ActivateRemote(catalog, (valid, validationError) =>
            {
                if (bundleCompleted || !CheckCurrent() || _activationSent) return;
                bundleCompleted = true;
                try
                {
                    if (!valid)
                    {
                        FailActivation("bundle validation or loading failed: " + validationError, catalogKeys);
                        return;
                    }
                    ScriptPackManager.PrepareRemote(catalog);
                    bool initializationStarted = false;
                    bool initializationCompleted = false;
                    void Complete(bool executionApproved)
                    {
                        if (initializationStarted || !CheckCurrent() || _activationSent) return;
                        initializationStarted = true;
                        try
                        {
                            ConnectionContentBootstrap.EnsureInitializing();
                            PasswordPrompt.Hide(ScriptPromptOwner);
                            ScriptingBootstrap.BeginPrepareConnection(catalog, executionApproved, activationId, (ready, scriptError) =>
                            {
                                if (initializationCompleted || !CheckCurrent() || _activationSent) return;
                                initializationCompleted = true;
                                try
                                {
                                    if (!ready || !ScriptingBootstrap.IsConnectionPrepared)
                                    {
                                        FailActivation("required script initialization failed: " + scriptError, catalogKeys);
                                        return;
                                    }
                                    ConnectionContentBootstrap.EnsureInitializing();
                                    if (!ClientContentRegistrar.TryActivate(catalog, out List<string> failedKeys, out string error))
                                    {
                                        if (CheckCurrent()) FailActivation("custom item activation rolled back: " + error, failedKeys);
                                        return;
                                    }
                                    if (!CheckCurrent()) return;
                                    ConnectionContentBootstrap.Loaded(activationId, catalogHash, manifestHash, true);
                                    if (!SendActivation(true, Array.Empty<string>()))
                                        throw new InvalidOperationException("The exact activation acknowledgement could not be delivered.");
                                    if (!CheckCurrent()) return;
                                    IsActivated = true;
                                    ReportHealth(FeatureHealthStatus.Healthy,
                                        "The authenticated bundles, item catalog and approved scripts were initialized.");
                                }
                                catch (Exception exception) { if (CheckCurrent()) FailActivation(exception.Message, catalogKeys); }
                            });
                        }
                        catch (Exception exception) { if (CheckCurrent()) FailActivation(exception.Message, catalogKeys); }
                    }
                    int scriptCount = catalog.ScriptPacks.Sum(pack => pack.Sources.Count);
                    if (scriptCount == 0) { Complete(false); return; }
                    PasswordPrompt.ShowConfirmation(ScriptPromptOwner,
                        Tr.Get(TrKeys.Content_ScriptApprovalTitle),
                        Tr.Get(TrKeys.Content_ScriptApprovalDetails, scriptCount,
                            catalog.ScriptPacks.Sum(pack => pack.Sources.Sum(source => (long)source.Size)), ServerTrustStore.FormatFingerprint(fingerprint)),
                        Tr.Get(TrKeys.Content_RunScripts), Tr.Get(TrKeys.Prompt_SkipContent),
                        () => Complete(true),
                        () =>
                        {
                            if (!initializationStarted && CheckCurrent() && !_activationSent)
                                DeclineByUser("user declined script execution");
                        });
                }
                catch (Exception exception) { if (CheckCurrent()) FailActivation(exception.Message, catalogKeys); }
            });
        }
        catch (Exception exception)
        {
            FailActivation(
                "required transfer-completion processing threw: " + exception.Message,
                catalogKeys);
        }
    }

    private static void FailActivation(string reason, IEnumerable<string> failedKeys)
    {
        if (_failureLatched) return;
        _failureLatched = true;
        RevokeMaterializedContent();
        bool ackQueued = false;
        try { ackQueued = SendActivation(false, failedKeys); }
        catch { }
        DisconnectAfterFailedAcknowledgement("Required custom content failed: " + reason, ackQueued);
        ReportHealth(FeatureHealthStatus.Failed, reason);
        SafeLogError("Required custom content failed: " + reason);
        ShowContentFailure(Tr.Get(TrKeys.Content_ActivationFailed), "CONTENT-ACTIVATION");
    }

    private static void DisconnectAfterFailedAcknowledgement(string reason, bool ackQueued)
    {
        var connection = NetworkClient.connection;
        if (!ackQueued || connection == null)
        {
            AnomalyMessaging.FailClosedTransport(reason);
            return;
        }
        try
        {
            // EncryptedMessageTransport.Send queues a Mirror batch. Update hands
            // it to LiteNetLib; immediate ClientDisconnect stops its send worker.
            // Revoke authentication now, then give that existing worker a bounded
            // opportunity to send the failure before closing this same connection.
            connection.Update();
            long transportGeneration = AnomalyMessaging.ClearTransportSession();
            if (MelonCoroutines.Start(DisconnectAfterAckFlush(connection, transportGeneration, reason)) == null)
                throw new InvalidOperationException("Could not schedule failure acknowledgement shutdown.");
        }
        catch (Exception exception)
        {
            SafeLogWarning("Failure acknowledgement flush failed: " + exception.Message);
            AnomalyMessaging.FailClosedTransport(reason);
        }
    }

    private static IEnumerator DisconnectAfterAckFlush(NetworkConnection connection, long transportGeneration, string reason)
    {
        IntPtr connectionPointer = connection.Pointer; // Keep the original IL2CPP connection alive for the complete grace interval.
        double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 0.25;
        while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline)
        {
            if (NetworkClient.connection == null || NetworkClient.connection.Pointer != connectionPointer ||
                AnomalyMessaging.TransportGeneration != transportGeneration)
                yield break;
            yield return null;
        }
        if (NetworkClient.connection != null && NetworkClient.connection.Pointer == connectionPointer &&
            AnomalyMessaging.TransportGeneration == transportGeneration)
            AnomalyMessaging.FailClosedTransport(reason);
        GC.KeepAlive(connection);
    }

    private static void ShowContentFailure(string reason, string code)
    {
        try
        {
            ConnectionInfoOverlay.ShowFailure(reason,
                Tr.Get(TrKeys.Content_FailureAction), code);
        }
        catch (Exception exception) { SafeLogError("Could not display content failure: " + exception); }
    }

    private static bool SendActivation(bool activated, IEnumerable<string> failedKeys)
    {
        if (_activationSent || _catalog == null)
            return false;
        _activationSent = true;
        return AnomalyMessaging.TrySend(new ContentCatalogAckMessage
        {
            CatalogSha256 = (byte[])_catalog.CatalogSha256.Clone(),
            ManifestSha256 = (byte[])_catalog.ManifestSha256.Clone(),
            Status = activated
                ? ContentCatalogActivationStatus.Activated
                : ContentCatalogActivationStatus.Failed,
            FailedKeys = failedKeys?.Distinct(StringComparer.Ordinal).Take(128).ToList() ?? new List<string>(),
        });
    }

    private static void ValidateCatalogFiles(ContentCatalog catalog, FileManifestMessage manifest)
    {
        var byHash = new Dictionary<string, FileManifestMessage.FileEntry>(StringComparer.Ordinal);
        foreach (FileManifestMessage.FileEntry file in manifest.Files)
            byHash.Add(FileCache.ToHex(file.Hash), file);

        var advertised = new HashSet<string>(StringComparer.Ordinal);
        foreach (CatalogPack pack in catalog.Packs)
        {
            string indexHash = FileCache.ToHex(pack.IndexHash);
            string bundleHash = FileCache.ToHex(pack.BundleHash);
            if (!advertised.Add(indexHash) || !advertised.Add(bundleHash) ||
                !byHash.TryGetValue(indexHash, out var index) || index.Kind != FileContentKind.PackIndex || index.Size != pack.IndexSize ||
                !byHash.TryGetValue(bundleHash, out var bundle) || bundle.Kind != FileContentKind.Bundle || bundle.Size != pack.BundleSize)
                throw new InvalidDataException($"Pack '{pack.PackId}' was not an exact advertised index/bundle pair.");
        }
        foreach (CatalogScriptPack pack in catalog.ScriptPacks)
        {
            Require(pack.IndexHash, FileContentKind.ScriptIndex, pack.IndexSize);
            foreach (CatalogScriptSource source in pack.Sources)
                if (source.Size != 0) Require(source.Hash, FileContentKind.LuaSource, source.Size);
        }
        if (advertised.Count != byHash.Count) throw new InvalidDataException("The manifest contained files outside the catalog's packs.");

        void Require(byte[] hash, FileContentKind kind, uint size)
        {
            string key = FileCache.ToHex(hash);
            if (!byHash.TryGetValue(key, out var file) || file.Kind != kind || file.Size != size)
                throw new InvalidDataException("Script pack was not an exact advertised index/source inventory.");
            advertised.Add(key); // Identical source bytes share one physical transfer file.
        }
    }

    private static void RevokeMaterializedContent()
    {
        IsActivated = false;
        try { ScriptingBootstrap.RevokeAll(); }
        catch (Exception exception) { SafeLogError("Script revocation failed: " + exception); }
        ScriptPackManager.Reset();
        try { AssetBundleManager.RevokeRemote(); ReleaseAssetBindings(); }
        catch (Exception exception) { SafeLogError("Content registrar cleanup failed: " + exception); }
    }

    private static void ReleaseAssetBindings()
    {
        // Restore every native consumer before unloading its pack resources.
        // If restoration throws, retain the packs and refuse the next activation.
        ClientContentRegistrar.Reset();
        DecorativeVisualClient.Clear();
        OverrideRegistry.Clear();
        AnnouncerVocabularyReceiver.Clear();
        CassieLineSourceSchedulePatch.RestoreAll();
        AssetBundleManager.StopRemoteAudio();
        AssetBundleManager.ClearRemote();
    }

    private static void ReportHealth(FeatureHealthStatus status, string detail)
    {
        try { FeatureHealth.Report("anomaly", "content", status, detail); }
        catch { }
    }

    private static void SafeLogWarning(string message)
    {
        try { MelonLogger.Warning(message); }
        catch { }
    }

    private static void SafeLogError(string message)
    {
        try { MelonLogger.Error(message); }
        catch { }
    }
}
