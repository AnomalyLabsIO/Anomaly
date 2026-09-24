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
    public void Registration_detaches_author_definition_before_catalog_freeze()
    {
        var definition = ValidDefinition("example:original");
        definition.DisplayName = "Original name";
        ContentRegistry.Add(definition);
        definition.Key = "example:changed";
        definition.DisplayName = "Changed name";

        ContentCatalog catalog = CreateOffer();

        Assert.Single(catalog.Entries);
        Assert.Equal("example:original", catalog.Entries[0].Key);
        Assert.Equal("Original name", catalog.Entries[0].DisplayName);
        Assert.True(ContentRegistry.TryGetId("example:original", out _));
        Assert.False(ContentRegistry.TryGetId("example:changed", out _));
    }

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
    public void Invalid_registration_throws_without_poisoning_existing_items()
    {
        ContentRegistry.Add(ValidDefinition("example:original"));
        Assert.Throws<ArgumentNullException>(() => ContentRegistry.Add(null));
        Assert.Throws<ArgumentException>(() => ContentRegistry.Add(ValidDefinition("INVALID KEY")));
        Assert.Throws<InvalidOperationException>(() => ContentRegistry.Add(ValidDefinition("example:original")));

        ContentCatalog catalog = CreateOffer();
        Assert.Single(catalog.Entries);
        Assert.Equal("example:original", catalog.Entries[0].Key);
        Assert.True(ContentRegistry.Materialized);
        Assert.Throws<InvalidOperationException>(() => ContentRegistry.Add(ValidDefinition("example:late")));
        Assert.False(ContentRegistry.TryGetId("example:late", out _));
    }

    [Theory]
    [InlineData("base")]
    [InlineData("empty-name")]
    [InlineData("long-key")]
    [InlineData("long-name")]
    [InlineData("long-description")]
    [InlineData("invalid-unicode")]
    public void Invalid_definition_fields_are_rejected_without_poisoning_the_catalog(string field)
    {
        ContentRegistry.Add(ValidDefinition("example:original"));
        var rejected = ValidDefinition("example:rejected");
        switch (field)
        {
            case "base": rejected.BaseTemplate = (ItemType)(-1); break;
            case "empty-name": rejected.DisplayName = " "; break;
            case "long-key": rejected.Key = string.Join(".", Enumerable.Repeat(new string('a', 32), 4)) + ":item"; break;
            case "long-name": rejected.DisplayName = new string('\u00e9', (ContentCatalogMessage.MaxNameBytes / 2) + 1); break;
            case "long-description": rejected.Description = new string('a', ContentCatalogMessage.MaxDescriptionBytes + 1); break;
            case "invalid-unicode": rejected.DisplayName = "\ud800"; break;
        }
        Assert.Throws<ArgumentException>(() => ContentRegistry.Add(rejected));
        Assert.False(ContentRegistry.Frozen);
        ContentCatalog catalog = CreateOffer();
        Assert.Single(catalog.Entries);
        Assert.Equal("example:original", catalog.Entries[0].Key);
        Assert.False(ContentRegistry.TryGetId(rejected.Key, out _));
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
        return CreateOffer();
    }

    private static CustomItemDefinition ValidDefinition(string key) => new()
    {
        Key = key,
        BaseTemplate = ItemType.Coin,
        DisplayName = "Example item",
        Description = "Acceptance fixture",
    };

    private static ContentCatalog CreateOffer()
    {
        using var snapshot = Anomaly.Server.FileTransfer.AssetPackSnapshot.Empty();
        return ContentRegistry.CreateOfferCatalog(EmptyManifestHash(), snapshot);
    }

    private static byte[] EmptyManifestHash() =>
        FileManifestMessage.ComputeManifestSha256(
            Array.Empty<FileManifestMessage.FileEntry>());

    private enum TestPreauthFlags : byte
    {
        None = 0,
        IgnoreSlotLimit = 1,
    }
}
