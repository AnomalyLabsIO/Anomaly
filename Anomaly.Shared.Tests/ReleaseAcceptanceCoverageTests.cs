using System;
using System.IO;
using System.Linq;
using Anomaly.Server.Auth;
using Anomaly.Server.Content;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using InventorySystem;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ReleaseAcceptanceCoverageTests : IDisposable
{
    [Fact]
    public void Reserved_slot_fullness_check_receives_authenticated_id_and_flags_once()
    {
        string observedId = null;
        TestPreauthFlags observedFlags = default;
        int calls = 0;

        bool full = ServerFullnessPolicy.IsFull(
            "accepted-leaf@anomaly",
            TestPreauthFlags.IgnoreSlotLimit,
            (userId, flags) =>
            {
                calls++;
                observedId = userId;
                observedFlags = flags;
                return true;
            });

        Assert.True(full);
        Assert.Equal(1, calls);
        Assert.Equal("accepted-leaf@anomaly", observedId);
        Assert.Equal(TestPreauthFlags.IgnoreSlotLimit, observedFlags);
    }

    [Theory]
    [InlineData(1200, 1136, true)]
    [InlineData(1200, 1137, false)]
    [InlineData(65535, 65471, true)]
    [InlineData(65535, 65472, false)]
    [InlineData(0, 0, false)]
    [InlineData(1200, -1, false)]
    [InlineData(int.MaxValue, int.MaxValue, false)]
    public void Transport_packet_policy_uses_each_reported_channel_ceiling(
        int reportedMaximum,
        int encodedBytes,
        bool expected)
    {
        Assert.Equal(expected, TransportPacketPolicy.Fits(reportedMaximum, encodedBytes));
    }

    [Fact]
    public void Transport_packet_policy_queries_the_selected_deployed_channel()
    {
        int observedChannel = -1;

        bool fits = TransportPacketPolicy.FitsReportedChannel(
            MessageChannel.ReliableUnordered,
            encodedBytes: 1136,
            channel =>
            {
                observedChannel = channel;
                return 1200;
            },
            out int reportedMaximum);

        Assert.True(fits);
        Assert.Equal((int)MessageChannel.ReliableUnordered, observedChannel);
        Assert.Equal(1200, reportedMaximum);
    }

    [Fact]
    public void Duplicate_item_registration_rejects_the_complete_catalog()
    {
        ContentRegistry.Add(ValidDefinition("example:duplicate"));
        ContentRegistry.Add(ValidDefinition("example:duplicate"));

        Assert.Throws<InvalidOperationException>(() =>
            ContentRegistry.CreateOfferCatalog(EmptyManifestHash()));
        Assert.False(ContentRegistry.Materialized);
        Assert.False(ContentRegistry.TryGetId("example:duplicate", out _));
    }

    [Fact]
    public void Declined_catalog_cannot_begin_transfer_or_grant_items()
    {
        ContentCatalog catalog = FreezeSingleItem();
        const uint netId = 41;

        ContentGrantGate.RecordOffer(netId, catalog);
        Assert.True(ContentGrantGate.TryRecordDecision(
            netId,
            catalog.CatalogSha256,
            catalog.ManifestSha256,
            approved: false));

        Assert.Equal(ContentReadiness.Declined, ContentGrantGate.GetReadiness(netId));
        Assert.False(ContentGrantGate.TryBeginTransfer(netId));
        Assert.False(ContentGrantGate.CanGrant(netId, "example:item"));
    }

    [Fact]
    public void Grants_require_matching_activation_and_activation_publishes_only_once()
    {
        ContentCatalog catalog = FreezeSingleItem();
        const uint netId = 42;

        ContentGrantGate.RecordOffer(netId, catalog);
        Assert.False(ContentGrantGate.CanGrant(netId, "example:item"));
        Assert.True(ContentGrantGate.TryRecordDecision(
            netId,
            catalog.CatalogSha256,
            catalog.ManifestSha256,
            approved: true));
        Assert.False(ContentGrantGate.CanGrant(netId, "example:item"));
        Assert.True(ContentGrantGate.TryBeginTransfer(netId));
        Assert.False(ContentGrantGate.CanGrant(netId, "example:item"));
        Assert.True(ContentGrantGate.TryCompleteTransfer(netId));
        Assert.False(ContentGrantGate.CanGrant(netId, "example:item"));

        int readyPublications = 0;
        Assert.True(ContentActivationCoordinator.TryCommit(
            netId,
            catalog.CatalogSha256,
            catalog.ManifestSha256,
            activated: true,
            () =>
            {
                readyPublications++;
                return true;
            },
            out bool firstPublicationSucceeded));
        Assert.True(firstPublicationSucceeded);
        Assert.True(ContentGrantGate.CanGrant(netId, "example:item"));
        Assert.False(ContentActivationCoordinator.TryCommit(
            netId,
            catalog.CatalogSha256,
            catalog.ManifestSha256,
            activated: true,
            () =>
            {
                readyPublications++;
                return true;
            },
            out _));
        Assert.Equal(1, readyPublications);
    }

    public void Dispose()
    {
        ContentGrantGate.Reset();
        ContentRegistry.Reset();
    }

    private static ContentCatalog FreezeSingleItem()
    {
        ContentRegistry.Add(ValidDefinition("example:item"));
        return ContentRegistry.CreateOfferCatalog(EmptyManifestHash());
    }

    private static CustomItemDefinition ValidDefinition(string key) => new()
    {
        Key = key,
        BaseTemplate = ItemType.Coin,
        DisplayName = "Example item",
        Description = "Acceptance fixture",
    };

    private static byte[] EmptyManifestHash() =>
        FileManifestMessage.ComputeManifestSha256(
            Array.Empty<FileManifestMessage.FileEntry>());

    private enum TestPreauthFlags : byte
    {
        None = 0,
        IgnoreSlotLimit = 1,
    }
}
