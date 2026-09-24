using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anomaly.Client.Assets;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Auth;
using Anomaly.Client.Components;
using Anomaly.Client.FileTransfer;
using Anomaly.Client.Internal.Audio;
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
    private static readonly TransactionalInitializer Initializer = new();
    private static ContentCatalog _catalog;
    private static FileManifestMessage _manifest;
    private static bool _decisionSent;
    private static bool _declined;
    private static bool _failureLatched;
    private static bool _activationSent;
    private static long _offerGeneration;
    internal static bool IsActivated { get; private set; }

    internal static void Init()
    {
        Initializer.Initialize(
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
        Exception cleanupFailure = null;
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

    private static void TryResolveOffer()
    {
        if (_catalog == null || _manifest == null || _decisionSent || _declined || _failureLatched)
            return;
        if (string.IsNullOrWhiteSpace(AuthHandler.CurrentServerFingerprint))
        {
            Fail("the authenticated server fingerprint is unavailable");
            return;
        }
        if (!CatalogBuilder.HashesEqual(_catalog.ManifestSha256, _manifest.ManifestSha256))
        {
            Fail("catalog and file manifest digests did not match");
            return;
        }

        try
        {
            ValidateCatalogAssets(_catalog, _manifest);
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
            return;
        }

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
        PasswordPrompt.ShowConfirmation(
            PromptOwner,
            "Approve this server's custom content?",
            $"Server fingerprint:\n{ServerTrustStore.FormatFingerprint(fingerprint)}\n\nItems: {_catalog.Entries.Count:N0}\nFiles: {_manifest.Files.Count:N0}\nDownload: {totalBytes / 1048576d:N2} MiB ({totalBytes:N0} bytes)"
                + "\n\nApproval is saved for this exact server and content version. Skipping may prevent joining servers that require this content.",
            Anomaly.Client.Api.Localization.Tr.Get(Anomaly.Client.Api.Internal.Localization.TrKeys.Prompt_AllowContent),
            Anomaly.Client.Api.Localization.Tr.Get(Anomaly.Client.Api.Internal.Localization.TrKeys.Prompt_SkipContent),
            () =>
            {
                if (offerGeneration != _offerGeneration)
                    return;
                if (_catalog == null ||
                    !CatalogBuilder.HashesEqual(catalogHash, _catalog.CatalogSha256) ||
                    !CatalogBuilder.HashesEqual(manifestHash, _catalog.ManifestSha256) ||
                    !string.Equals(fingerprint, AuthHandler.CurrentServerFingerprint, StringComparison.Ordinal))
                {
                    Fail("the authenticated offer changed while consent was pending");
                    return;
                }
                if (!ContentConsentStore.TryRemember(fingerprint, catalogHash, manifestHash))
                {
                    Fail("consent could not be persisted safely");
                    return;
                }
                Approve();
            },
            () =>
            {
                if (offerGeneration == _offerGeneration)
                    DeclineByUser("user declined the content offer");
            });
    }

    private static void Approve()
    {
        if (_decisionSent || _catalog == null)
            return;
        _decisionSent = true;
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
        bool delivered = SendDecline();
        if (!delivered)
        {
            const string failure = "The content decline could not be delivered over the authenticated transport.";
            AnomalyMessaging.FailClosedTransport(failure);
            ReportHealth(FeatureHealthStatus.Failed, failure);
            return;
        }
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
        ShowContentFailure("The server's custom content could not be validated.", "CONTENT-VALIDATION");
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
        if (_declined || _activationSent || _catalog == null)
            return;
        string[] catalogKeys = _catalog.Packs.Select(pack => pack.PackId)
            .Concat(_catalog.Entries.Select(entry => entry.Key)).Distinct(StringComparer.Ordinal).Take(128).ToArray();
        try
        {
            if (!successful)
            {
                FailActivation("the approved file transfer failed", catalogKeys);
                return;
            }

            long generation = _offerGeneration;
            ContentCatalog catalog = _catalog;
            AssetBundleManager.ActivateRemote(catalog, (valid, validationError) =>
            {
                if (generation != _offerGeneration || _declined || _activationSent) return;
                try
                {
                    if (!valid)
                    {
                        FailActivation("bundle validation or loading failed: " + validationError, catalogKeys);
                        return;
                    }
                    if (!ClientContentRegistrar.TryActivate(catalog, out List<string> failedKeys, out string error))
                    {
                        FailActivation("custom item activation rolled back: " + error, failedKeys);
                        return;
                    }
                    if (!SendActivation(true, Array.Empty<string>()))
                    {
                        const string failure = "The exact activation acknowledgement could not be delivered.";
                        RevokeMaterializedContent();
                        AnomalyMessaging.FailClosedTransport(failure);
                        ReportHealth(FeatureHealthStatus.Failed, failure);
                        return;
                    }
                    IsActivated = true;
                    ReportHealth(FeatureHealthStatus.Healthy,
                        "The authenticated bundles and item catalog were activated transactionally.");
                }
                catch (Exception exception) { FailActivation(exception.Message, catalogKeys); }
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
        ShowContentFailure("The approved custom content could not be activated.", "CONTENT-ACTIVATION");
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
            AnomalyMessaging.ClearTransportSession();
            if (MelonCoroutines.Start(DisconnectAfterAckFlush(connection, reason)) == null)
                throw new InvalidOperationException("Could not schedule failure acknowledgement shutdown.");
        }
        catch (Exception exception)
        {
            SafeLogWarning("Failure acknowledgement flush failed: " + exception.Message);
            AnomalyMessaging.FailClosedTransport(reason);
        }
    }

    private static IEnumerator DisconnectAfterAckFlush(NetworkConnection connection, string reason)
    {
        IntPtr connectionPointer = connection.Pointer; // Keep the original IL2CPP connection alive for the complete grace interval.
        double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 0.25;
        while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline)
        {
            if (NetworkClient.connection == null || NetworkClient.connection.Pointer != connectionPointer)
                yield break;
            yield return null;
        }
        if (NetworkClient.connection != null && NetworkClient.connection.Pointer == connectionPointer)
            AnomalyMessaging.FailClosedTransport(reason);
        GC.KeepAlive(connection);
    }

    private static void ShowContentFailure(string reason, string code)
    {
        try
        {
            ConnectionInfoOverlay.ShowFailure(reason,
                "Check the game log for the content error, then reconnect. If this repeats, ask the server operator to check the catalog and files.", code);
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

    private static void ValidateCatalogAssets(ContentCatalog catalog, FileManifestMessage manifest)
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
        if (advertised.Count != byHash.Count) throw new InvalidDataException("The manifest contained files outside the catalog's packs.");
    }

    private static void RevokeMaterializedContent()
    {
        IsActivated = false;
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
