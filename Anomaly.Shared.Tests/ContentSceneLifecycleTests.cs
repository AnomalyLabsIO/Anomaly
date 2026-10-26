using System.Linq;
using System.Reflection;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Components;
using Anomaly.Client.Content;
using Anomaly.Client.FileTransfer;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ContentSceneLifecycleTests : IDisposable
{
    public ContentSceneLifecycleTests()
    {
        ContentCatalogClient.Init();
        Dispose();
        ContentConsentFixture.Begin();
    }

    [Fact]
    public void PendingOfferIsReplacedOnlyAtSceneBoundaryAndOldConsentCallbackCannotApproveIt()
    {
        Offer(1);
        Action oldApproval = PasswordPrompt.Approve;
        Assert.Empty(AnomalyMessaging.Sent);
        ConnectionContentBootstrap.BeginFacilityGeneration(2);
        Offer(2);

        oldApproval();
        Assert.Empty(AnomalyMessaging.Sent);
        Assert.Equal(2, PasswordPrompt.Shown);
        Assert.Equal("AwaitingConsent", TransferState());
        Assert.Empty(ContentConsentStore.Approved);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void InitialFacilityAwakePreservesTheEarlyAuthenticatedOffer()
    {
        FileCache.HasFileResult = _ => true;
        Offer(3);
        Action approval = PasswordPrompt.Approve;
        ContentCatalogClient.BeginFacilityScene();
        Assert.Same(approval, PasswordPrompt.Approve);
        Assert.Equal("AwaitingConsent", TransferState());
        approval();
        Assert.True(ContentCatalogClient.IsActivated);
        Assert.Equal(1, ClientContentRegistrar.Activations);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void Declined_offer_closes_the_connection_without_requesting_any_bytes()
    {
        Offer(10);
        PasswordPrompt.Decline();
        Assert.Equal("Idle", TransferState());
        Assert.Equal(ContentCatalogDecision.Declined,
            Assert.Single(AnomalyMessaging.Sent.OfType<ContentCatalogDecisionMessage>()).Decision);

        Assert.Equal(1, PasswordPrompt.Shown);
        Assert.Empty(AnomalyMessaging.Sent.OfType<FileRequestMessage>());
        ContentConsentFixture.DrainGrace();
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void MatchingCachedApprovalReactivatesNewHubAndNeverLeavesLoadingOverlayVisible()
    {
        FileCache.HasFileResult = _ => true;
        var first = Offer(20);
        FileTransferOverlay.Show();
        PasswordPrompt.Approve();
        Assert.True(FileTransferClient.IsFileApproved(first.Files[0].Hash, FileContentKind.Bundle));
        Assert.False(FileTransferOverlay.Visible);
        Assert.Equal(1, ClientContentRegistrar.Activations);

        ConnectionContentBootstrap.BeginFacilityGeneration(2);
        Assert.False(FileTransferClient.IsFileApproved(first.Files[0].Hash, FileContentKind.Bundle));
        Assert.False(ClientContentRegistrar.Active);
        FileTransferOverlay.Show();
        Offer(21);

        Assert.Equal(1, PasswordPrompt.Shown);
        Assert.Equal(2, ClientContentRegistrar.Activations);
        Assert.Equal(new ulong[] { 20, 21 }, AnomalyMessaging.Sent.OfType<FileRequestMessage>().Select(msg => msg.SessionId));
        Assert.All(AnomalyMessaging.Sent.OfType<FileRequestMessage>(), msg => Assert.Empty(msg.RequestedHashes));
        Assert.Equal(2, AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>().Count(msg => msg.Status == ContentCatalogActivationStatus.Activated));
        Assert.True(FileTransferClient.IsFileApproved(first.Files[0].Hash, FileContentKind.Bundle));
        Assert.False(FileTransferOverlay.Visible);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void DownloadCompletionAfterFacilityReadyHidesOverlayAndActivatesContent()
    {
        byte[] bytes = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        var manifest = Offer(25, content: bytes);
        PasswordPrompt.Approve();
        FileTransferOverlay.Show();
        Invoke(typeof(FileTransferClient), "HandleFileComplete", new FileTransferCompleteMessage
        {
            SessionId = manifest.SessionId, Hash = manifest.Files[0].Hash,
        });
        Invoke(typeof(FileTransferClient), "HandleChunk", new FileChunkMessage
        {
            SessionId = manifest.SessionId, Hash = manifest.Files[0].Hash,
            ChunkIndex = 0, TotalChunks = 1, Data = bytes,
        });

        Assert.Equal("Complete", TransferState());
        Assert.False(FileTransferOverlay.Visible);
        Assert.Equal(1, ClientContentRegistrar.Activations);
        Assert.Equal(ContentCatalogActivationStatus.Activated,
            Assert.Single(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>()).Status);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void ChangedManifestNeedsNewConsentEvenWhenEveryBlobIsCached()
    {
        FileCache.HasFileResult = _ => true;
        var first = Offer(30);
        PasswordPrompt.Approve();
        ConnectionContentBootstrap.BeginFacilityGeneration(2);
        Offer(31, hashByte: 0x22);

        Assert.Equal(2, PasswordPrompt.Shown);
        Assert.Equal("AwaitingConsent", TransferState());
        Assert.Single(AnomalyMessaging.Sent.OfType<FileRequestMessage>());
        Assert.False(FileTransferClient.IsFileApproved(first.Files[0].Hash, FileContentKind.Bundle));
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void DuplicateManifestAloneCannotResetOrApproveTheCurrentOffer()
    {
        FileCache.HasFileResult = _ => true;
        var first = Offer(40);
        PasswordPrompt.Approve();
        first.SessionId = 41;
        Invoke(typeof(FileTransferClient), "HandleManifest", first);

        Assert.Equal("Complete", TransferState());
        Assert.Equal(40UL, Assert.Single(AnomalyMessaging.Sent.OfType<FileRequestMessage>()).SessionId);
        Assert.Equal(1, ClientContentRegistrar.Activations);
    }

    [Fact]
    public void FailedTemplateCleanupRevokesOfferAndPreventsSceneReadiness()
    {
        FileCache.HasFileResult = _ => true;
        var first = Offer(50);
        PasswordPrompt.Approve();
        ClientContentRegistrar.ThrowOnReset = true;

        FileTransferClient.Reset();
        Assert.Throws<InvalidOperationException>(ContentCatalogClient.ResetForShutdown);
        Assert.Equal("Idle", TransferState());
        Assert.False(FileTransferClient.IsFileApproved(first.Files[0].Hash, FileContentKind.Bundle));
        Assert.Null(PasswordPrompt.Approve);
        Assert.False(ClientContentRegistrar.Active);
    }

    private static FileManifestMessage Offer(ulong sessionId, byte hashByte = 0x11, byte[] content = null)
    {
        byte[] hash = Enumerable.Repeat(hashByte, 32).ToArray();
        if (content != null)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            hash = sha.ComputeHash(content);
        }
        var manifest = new FileManifestMessage
        {
            SessionId = sessionId,
            Files = { new FileManifestMessage.FileEntry("test.bundle", hash, (uint)(content?.Length ?? 16), FileContentKind.Bundle) },
        };
        byte[] indexHash = Enumerable.Repeat((byte)(hashByte + 1), 32).ToArray();
        manifest.Files.Add(new FileManifestMessage.FileEntry("test.index.json", indexHash, 16, FileContentKind.PackIndex));
        if (content != null) FileCache.HasFileResult = candidate => candidate.SequenceEqual(indexHash);
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
        var catalog = CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 1, 0, manifest.ManifestSha256,
            new[] { new CatalogPack { PackId = "fixture:test", IndexHash = indexHash, IndexSize = 16, BundleHash = hash, BundleSize = manifest.Files[0].Size } });
        Invoke(typeof(ContentCatalogClient), "HandleMessage", new ContentCatalogMessage { Catalog = catalog });
        Invoke(typeof(FileTransferClient), "HandleManifest", manifest);
        return manifest;
    }

    private static void Invoke(Type owner, string method, object value) =>
        owner.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { value });
    private static string TransferState() => typeof(FileTransferClient)
        .GetField("_state", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null).ToString();

    public void Dispose() => ContentConsentFixture.Reset();
}
