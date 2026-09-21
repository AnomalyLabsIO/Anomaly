using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Auth;
using Anomaly.Client.Components;
using Anomaly.Client.FileTransfer;
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

    internal static void Reset()
    {
        if (_offerGeneration < long.MaxValue)
            _offerGeneration++;
        _catalog = null;
        _manifest = null;
        _decisionSent = false;
        _declined = false;
        _failureLatched = false;
        _activationSent = false;
        try { PasswordPrompt.Hide(PromptOwner); }
        catch (Exception exception)
        {
            SafeLogError("Content prompt cleanup failed after logical state was revoked: " + exception);
        }
        try { ClientContentRegistrar.Reset(); }
        catch (Exception exception)
        {
            SafeLogError("Content registrar cleanup failed after logical state was revoked: " + exception);
        }
    }

    private static void HandleMessage(IAnomalyMessage message)
    {
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
        if (_catalog == null || _manifest == null || _decisionSent || _declined)
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
            $"Server: {fingerprint}\nItems: {_catalog.Entries.Count}\nFiles: {_manifest.Files.Count}\nEncoded bytes: {totalBytes:N0}",
            "Press Enter to approve this exact server/catalog/manifest tuple. Press Esc to decline.",
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
        string[] catalogKeys = _catalog.Entries.Select(entry => entry.Key).ToArray();
        try
        {
            if (!successful)
            {
                FailActivation("the approved file transfer failed", catalogKeys);
                return;
            }

            if (!ClientContentRegistrar.TryActivate(_catalog, out List<string> failedKeys, out string error))
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
            ReportHealth(FeatureHealthStatus.Healthy,
                "The authenticated item catalog was activated transactionally.");
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
        RevokeMaterializedContent();
        try { SendActivation(false, failedKeys); }
        catch { }
        AnomalyMessaging.FailClosedTransport("Required custom content failed: " + reason);
        ReportHealth(FeatureHealthStatus.Failed, reason);
        SafeLogError("Required custom content failed: " + reason);
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
            FailedKeys = failedKeys?.Distinct(StringComparer.Ordinal).ToList() ?? new List<string>(),
        });
    }

    private static void ValidateCatalogAssets(ContentCatalog catalog, FileManifestMessage manifest)
    {
        var byHash = new Dictionary<string, FileManifestMessage.FileEntry>(StringComparer.Ordinal);
        foreach (FileManifestMessage.FileEntry file in manifest.Files)
            byHash.Add(FileCache.ToHex(file.Hash), file);

        foreach (CatalogEntry entry in catalog.Entries)
        {
            if (entry.IconHash == null)
                continue;
            if (!byHash.TryGetValue(FileCache.ToHex(entry.IconHash), out FileManifestMessage.FileEntry file) ||
                file.Kind != AssetKind.Texture2D ||
                file.Size != entry.IconSize ||
                !string.Equals(Path.GetExtension(file.Path), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Catalog icon for '{entry.Key}' was not an exact advertised PNG.");
        }
    }

    private static void RevokeMaterializedContent()
    {
        try { ClientContentRegistrar.Reset(); }
        catch (Exception exception) { SafeLogError("Content registrar cleanup failed: " + exception); }
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
