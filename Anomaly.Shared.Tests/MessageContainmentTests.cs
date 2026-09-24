using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class MessageContainmentTests
{
    [Fact]
    public void Canonical_custom_item_base_depends_only_on_the_stock_enum_maximum()
    {
        Assert.Equal(61, CatalogBuilder.ComputeIdBase(60));
        Assert.Throws<InvalidOperationException>(() => CatalogBuilder.ComputeIdBase(int.MaxValue));
    }

    [Fact]
    public void ReplayTransmitCounterSaturatesBeforeWireWrap()
    {
        long counter = uint.MaxValue - 2L;

        Assert.Equal(uint.MaxValue - 1U, AnomalyReplayState.TryAdvanceTxCounter(ref counter));
        Assert.Equal(0U, AnomalyReplayState.TryAdvanceTxCounter(ref counter));
        Assert.Equal(uint.MaxValue - 1L, counter);
    }

    [Fact]
    public void ReplayReceiveRejectsReservedMaximumCounter()
    {
        var state = new AnomalyReplayState();

        Assert.Equal(
            AnomalyReplayState.RxResult.RejectedInvalid,
            state.TryAcceptRx(MessageChannel.ReliableOrdered, uint.MaxValue, out _));
    }

    [Fact]
    public void Every_built_in_message_declares_fail_closed_policy()
    {
        Type[] messages = typeof(IAnomalyMessage).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IAnomalyMessage).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(messages);
        foreach (Type type in messages)
        {
            var message = (IAnomalyMessage)Activator.CreateInstance(type);
            var directional = Assert.IsAssignableFrom<IDirectionalAnomalyMessage>(message);
            var phased = Assert.IsAssignableFrom<IAuthPhaseAnomalyMessage>(message);
            var bounded = Assert.IsAssignableFrom<IBoundedAnomalyMessage>(message);

            Assert.NotEqual(AnomalyMessageDirection.Bidirectional, directional.Direction);
            Assert.Equal(AnomalyMessageAuthPhase.AuthenticatedSession, phased.AuthPhase);
            Assert.InRange(bounded.MaxPayloadBytes, 0, AnomalyChannel.MaxEnvelopeBytes);
        }
    }

    [Fact]
    public void Registry_rejects_bidirectional_message_policy()
    {
        string name = "test:bidirectional:" + Guid.NewGuid().ToString("N");
        var error = Assert.Throws<InvalidOperationException>(() =>
            AnomalyMessageRegistry.Register(name, () => new BidirectionalTestMessage(name)));

        Assert.Contains("exactly one wire direction", error.Message);
        Assert.Throws<KeyNotFoundException>(() => AnomalyMessageRegistry.GetHash(name));
    }

    [Theory]
    [InlineData("anomaly:content:catalog")]
    [InlineData("not-namespaced")]
    [InlineData("Example:message")]
    [InlineData("example::message")]
    public void Public_registry_rejects_reserved_or_malformed_names(string name)
    {
        Assert.ThrowsAny<Exception>(() =>
            AnomalyMessageRegistry.Register(name, () => new TestClientMessage(name)));
    }

    [Fact]
    public void Exact_duplicate_registration_requires_the_same_type_and_policy()
    {
        string name = "test:duplicate:" + Guid.NewGuid().ToString("N");
        AnomalyMessageRegistry.Register(name, () => new TestClientMessage(name));
        AnomalyMessageRegistry.Register(name, () => new TestClientMessage(name));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            AnomalyMessageRegistry.Register(name, () => new TestServerMessage(name)));
        Assert.Contains("different message contract", error.Message);
    }

    [Fact]
    public void Manifest_round_trip_binds_session_hash_size_kind_and_safe_path()
    {
        byte[] hash = Enumerable.Repeat((byte)0xA5, 32).ToArray();
        var source = new FileManifestMessage
        {
            SessionId = 42,
            Files =
            {
                new FileManifestMessage.FileEntry("audio/word.bundle", hash, 1024, FileContentKind.Bundle)
            }
        };
        source.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(source.Files);

        FileManifestMessage decoded = RoundTrip(source, () => new FileManifestMessage());

        Assert.Equal(42ul, decoded.SessionId);
        Assert.Single(decoded.Files);
        Assert.Equal("audio/word.bundle", decoded.Files[0].Path);
        Assert.Equal(hash, decoded.Files[0].Hash);
        Assert.Equal(1024u, decoded.Files[0].Size);
        Assert.Equal(FileContentKind.Bundle, decoded.Files[0].Kind);
    }

    [Fact]
    public void Approved_manifest_snapshot_requires_exact_hash_and_asset_kind()
    {
        byte[] textureHash = Enumerable.Repeat((byte)0xA1, 32).ToArray();
        byte[] audioHash = Enumerable.Repeat((byte)0xB2, 32).ToArray();
        var snapshot = new AssetManifestSnapshot(new[]
        {
            new FileManifestMessage.FileEntry("textures/item.index.json", textureHash, 64, FileContentKind.PackIndex),
            new FileManifestMessage.FileEntry("audio/word.bundle", audioHash, 64, FileContentKind.Bundle),
        });

        Assert.Equal(2, snapshot.Count);
        Assert.True(snapshot.Contains((byte[])textureHash.Clone(), FileContentKind.PackIndex));
        Assert.True(snapshot.Contains((byte[])audioHash.Clone(), FileContentKind.Bundle));
        Assert.False(snapshot.Contains(textureHash, FileContentKind.Bundle));
        Assert.False(snapshot.Contains(audioHash, FileContentKind.PackIndex));
        Assert.False(snapshot.Contains(Enumerable.Repeat((byte)0xCC, 32).ToArray(), FileContentKind.PackIndex));
        Assert.False(snapshot.Contains(new byte[31], FileContentKind.PackIndex));
    }

    [Fact]
    public void Clear_all_overrides_round_trip_fits_declared_policy()
    {
        var source = new AssetClearAllOverridesMessage { NetId = 0xDEADBEEF };

        byte[] payload = Serialize(source);
        AssetClearAllOverridesMessage decoded = RoundTrip(
            source,
            () => new AssetClearAllOverridesMessage());

        Assert.Equal(sizeof(uint), source.MaxPayloadBytes);
        Assert.Equal(source.MaxPayloadBytes, payload.Length);
        Assert.Equal(source.NetId, decoded.NetId);
    }

    [Theory]
    [InlineData("../escape.bundle")]
    [InlineData("folder//file.bundle")]
    [InlineData("C:\\absolute.bundle")]
    [InlineData("line\nbreak.bundle")]
    public void Manifest_rejects_unsafe_metadata_paths(string path)
    {
        var message = new FileManifestMessage
        {
            SessionId = 1,
            Files =
            {
                new FileManifestMessage.FileEntry(
                    path,
                    new byte[32],
                    1,
                    FileContentKind.PackIndex)
            }
        };

        Assert.Throws<InvalidDataException>(() => Serialize(message));
    }

    [Fact]
    public void Request_rejects_duplicate_hashes()
    {
        var hash = new byte[32];
        var request = new FileRequestMessage
        {
            SessionId = 7,
            RequestedHashes = { hash, (byte[])hash.Clone() }
        };

        Assert.Throws<InvalidDataException>(() => Serialize(request));
    }

    [Fact]
    public void Consent_request_file_ack_and_activation_share_the_ordered_channel()
    {
        Assert.Equal(MessageChannel.ReliableOrdered, new ContentCatalogDecisionMessage().TransportChannel);
        Assert.Equal(MessageChannel.ReliableOrdered, new FileRequestMessage().TransportChannel);
        Assert.Equal(MessageChannel.ReliableOrdered, new FileAckMessage().TransportChannel);
        Assert.Equal(MessageChannel.ReliableOrdered, new ContentCatalogAckMessage().TransportChannel);
    }

    [Fact]
    public void Chunk_rejects_data_over_the_streaming_ceiling()
    {
        var chunk = new FileChunkMessage
        {
            SessionId = 7,
            Hash = new byte[32],
            ChunkIndex = 0,
            TotalChunks = 1,
            Data = new byte[FileChunkMessage.MaxChunkBytes + 1]
        };

        Assert.Throws<InvalidDataException>(() => Serialize(chunk));
    }

    [Fact]
    public void Content_catalog_round_trip_preserves_canonical_items_only_contract()
    {
        byte[] manifest = Enumerable.Repeat((byte)0x44, 32).ToArray();
        ContentCatalog catalog = CatalogBuilder.Build(
            new[]
            {
                new CatalogEntry
                {
                    Key = "example:item",
                    BaseTemplateId = 1,
                    DisplayName = "Example item",
                    Description = "Items only",
                    IconAddress = "mods/example/icons/item",

                }
            },
            120,
            127,
            manifest);

        ContentCatalogMessage decoded = RoundTrip(
            new ContentCatalogMessage { Catalog = catalog },
            () => new ContentCatalogMessage());

        CatalogEntry item = Assert.Single(decoded.Catalog.Entries);
        Assert.Equal("example:item", item.Key);
        Assert.Equal(120, item.AssignedId);
        Assert.Equal(1, item.BaseTemplateId);
        Assert.Equal(catalog.CatalogSha256, decoded.Catalog.CatalogSha256);
        Assert.Equal(manifest, decoded.Catalog.ManifestSha256);
    }

    [Fact]
    public void Content_catalog_rejects_duplicate_ids_even_with_recomputed_hash()
    {
        byte[] manifest = Enumerable.Repeat((byte)0x66, 32).ToArray();
        var catalog = new ContentCatalog
        {
            ManifestSha256 = manifest,
            Entries =
            {
                new CatalogEntry { Key = "example:first", AssignedId = 120, BaseTemplateId = 1, DisplayName = "First" },
                new CatalogEntry { Key = "example:second", AssignedId = 120, BaseTemplateId = 2, DisplayName = "Second" },
            },
        };
        catalog.CatalogSha256 = CatalogBuilder.ComputeCatalogHash(catalog);

        Assert.Throws<InvalidDataException>(() => Serialize(new ContentCatalogMessage { Catalog = catalog }));
    }

    [Fact]
    public void Content_catalog_builder_rejects_duplicate_keys_before_assigning_ids()
    {
        var entries = new[]
        {
            new CatalogEntry { Key = "example:duplicate", BaseTemplateId = 1, DisplayName = "First" },
            new CatalogEntry { Key = "example:duplicate", BaseTemplateId = 2, DisplayName = "Second" },
        };

        Assert.Throws<InvalidDataException>(() => CatalogBuilder.Build(
            entries,
            120,
            127,
            FileManifestMessage.ComputeManifestSha256(
                Array.Empty<FileManifestMessage.FileEntry>())));
        Assert.All(entries, entry => Assert.Equal(0, entry.AssignedId));
    }

    [Fact]
    public void Content_catalog_rejects_noncanonical_key_order_even_with_recomputed_hash()
    {
        ContentCatalog catalog = CatalogBuilder.Build(
            new[]
            {
                new CatalogEntry { Key = "example:first", BaseTemplateId = 1, DisplayName = "First" },
                new CatalogEntry { Key = "example:second", BaseTemplateId = 2, DisplayName = "Second" },
            },
            120,
            127,
            FileManifestMessage.ComputeManifestSha256(Array.Empty<FileManifestMessage.FileEntry>()));
        catalog.Entries.Reverse();
        catalog.CatalogSha256 = CatalogBuilder.ComputeCatalogHash(catalog);

        Assert.Throws<InvalidDataException>(() =>
            ContentCatalogMessage.ValidateCatalog(catalog, validateDeclaredHash: true));
    }

    [Fact]
    public void Content_catalog_rejects_noncontiguous_assigned_ids_even_with_recomputed_hash()
    {
        byte[] manifest = FileManifestMessage.ComputeManifestSha256(
            Array.Empty<FileManifestMessage.FileEntry>());
        ContentCatalog catalog = CatalogBuilder.Build(
            new[]
            {
                new CatalogEntry { Key = "example:first", BaseTemplateId = 1, DisplayName = "First" },
                new CatalogEntry { Key = "example:second", BaseTemplateId = 2, DisplayName = "Second" },
            },
            120,
            127,
            manifest);
        catalog.Entries[1].AssignedId++;
        catalog.CatalogSha256 = CatalogBuilder.ComputeCatalogHash(catalog);

        Assert.Throws<InvalidDataException>(() =>
            ContentCatalogMessage.ValidateCatalog(catalog, validateDeclaredHash: true));
    }

    [Fact]
    public void Content_catalog_accepts_largest_full_text_case_below_payload_ceiling()
    {
        ContentCatalog catalog = BuildFullTextCatalog(56);

        byte[] payload = Serialize(new ContentCatalogMessage { Catalog = catalog });

        Assert.Equal(259853, payload.Length);
        Assert.True(payload.Length <= ContentCatalogMessage.MaxCatalogPayloadBytes);
    }

    [Fact]
    public void Content_catalog_rejects_first_full_text_case_above_payload_ceiling()
    {
        ContentCatalog catalog = BuildFullTextCatalog(57);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            ContentCatalogMessage.ValidateCatalog(catalog, validateDeclaredHash: true));

        Assert.Contains("payload ceiling", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Consent_activation_and_enrollment_messages_round_trip_strictly()
    {
        byte[] catalogHash = Enumerable.Repeat((byte)0x77, 32).ToArray();
        byte[] manifestHash = Enumerable.Repeat((byte)0x88, 32).ToArray();
        ContentCatalogDecisionMessage decision = RoundTrip(new ContentCatalogDecisionMessage
        {
            CatalogSha256 = catalogHash,
            ManifestSha256 = manifestHash,
            Decision = ContentCatalogDecision.Approved,
        }, () => new ContentCatalogDecisionMessage());
        Assert.Equal(ContentCatalogDecision.Approved, decision.Decision);

        ContentCatalogAckMessage activation = RoundTrip(new ContentCatalogAckMessage
        {
            CatalogSha256 = catalogHash,
            ManifestSha256 = manifestHash,
            Status = ContentCatalogActivationStatus.Activated,
        }, () => new ContentCatalogAckMessage());
        Assert.Empty(activation.FailedKeys);

        EnrollmentIdentityMessage enrollment = RoundTrip(
            new EnrollmentIdentityMessage { EnrollmentId = "local-player@anomaly" },
            () => new EnrollmentIdentityMessage());
        Assert.Equal("local-player@anomaly", enrollment.EnrollmentId);
    }

    [Fact]
    public void Inbound_rate_limiter_bounds_aggregate_bytes_and_escalates_repeated_drops()
    {
        var limiter = new AnomalyInboundRateLimiter();
        for (uint i = 0; i < 4; i++)
            Assert.True(limiter.TryAccept(1, AnomalyChannel.MaxEnvelopeBytes).Allowed);

        AnomalyRateLimitResult firstDrop = limiter.TryAccept(1, AnomalyChannel.MaxEnvelopeBytes);
        Assert.False(firstDrop.Allowed);
        Assert.Equal(1, firstDrop.DroppedInWindow);

        AnomalyRateLimitResult result = firstDrop;
        for (int i = 1; i < 32; i++)
            result = limiter.RegisterProtocolViolation();
        Assert.True(result.ShouldDisconnect);
        Assert.Equal(32, result.DroppedInWindow);
    }

    [Fact]
    public void Inbound_rate_limiter_bounds_per_peer_hash_state()
    {
        var limiter = new AnomalyInboundRateLimiter();
        for (uint hash = 0; hash < AnomalyInboundRateLimiter.MaxTrackedHashes; hash++)
            Assert.True(limiter.TryAccept(hash, 0).Allowed);

        AnomalyRateLimitResult overflow = limiter.TryAccept(
            AnomalyInboundRateLimiter.MaxTrackedHashes,
            0);
        Assert.False(overflow.Allowed);
        Assert.Equal(1, overflow.DroppedInWindow);
    }

    [Fact]
    public void Capability_snapshot_rejects_oversized_string_prefix_before_allocation()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1u);
            writer.Write(1u);
            writer.Write((byte)1);
            // Seven-bit encoded 16,383-byte declaration with no payload.
            writer.Write((byte)0xff);
            writer.Write((byte)0x7f);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.Throws<InvalidDataException>(() => new CapabilitySnapshotMessage().Deserialize(reader));
        Assert.Equal(11, stream.Position);
    }

    [Fact]
    public void Text_bearing_messages_reject_malformed_utf8()
    {
        Assert.Throws<DecoderFallbackException>(() => DeserializeMalformedText(
            new AssetOverrideTextureMessage(),
            writer =>
            {
                writer.Write(1u);
                WriteMalformedLengthPrefixedUtf8(writer);
                writer.Write((ushort)0);
                writer.Write(new byte[32]);
            }));

        Assert.Throws<DecoderFallbackException>(() => DeserializeMalformedText(
            new AssetClearOverrideMessage(),
            writer =>
            {
                writer.Write(1u);
                WriteMalformedLengthPrefixedUtf8(writer);
                writer.Write((ushort)0);
            }));

        Assert.Throws<DecoderFallbackException>(() => DeserializeMalformedText(
            new AnnouncerVocabularyMessage(),
            writer =>
            {
                writer.Write((ushort)1);
                WriteMalformedLengthPrefixedUtf8(writer);
                writer.Write(new byte[32]);
                writer.Write(1f);
                writer.Write((byte)0);
            }));

        Assert.Throws<DecoderFallbackException>(() => DeserializeMalformedText(
            new CapabilitySnapshotMessage(),
            writer =>
            {
                writer.Write(1u);
                writer.Write(1u);
                writer.Write((byte)1);
                writer.Write((byte)2);
                writer.Write((byte)0xC3);
                writer.Write((byte)0x28);
                writer.Write((ushort)1);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
            }));
    }

    private static ContentCatalog BuildFullTextCatalog(int count)
    {
        CatalogEntry[] entries = Enumerable.Range(0, count)
            .Select(index => new CatalogEntry
            {
                Key = $"example:item{index:D3}",
                BaseTemplateId = 1,
                DisplayName = new string('N', ContentCatalogMessage.MaxNameBytes),
                Description = new string('D', ContentCatalogMessage.MaxDescriptionBytes),
            })
            .ToArray();
        return CatalogBuilder.Build(
            entries,
            1,
            127,
            FileManifestMessage.ComputeManifestSha256(Array.Empty<FileManifestMessage.FileEntry>()));
    }

    private static void DeserializeMalformedText(IAnomalyMessage message, Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            write(writer);
            writer.Flush();
        }
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        message.Deserialize(reader);
    }

    private static void WriteMalformedLengthPrefixedUtf8(BinaryWriter writer)
    {
        writer.Write((ushort)2);
        writer.Write((byte)0xC3);
        writer.Write((byte)0x28);
    }

    private static byte[] Serialize(IAnomalyMessage message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        message.Serialize(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static T RoundTrip<T>(T source, Func<T> factory) where T : IAnomalyMessage
    {
        byte[] bytes = Serialize(source);
        T destination = factory();
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        destination.Deserialize(reader);
        Assert.Equal(stream.Length, stream.Position);
        return destination;
    }

    private sealed class BidirectionalTestMessage : IAnomalyMessage,
        IDirectionalAnomalyMessage,
        IAuthPhaseAnomalyMessage,
        IBoundedAnomalyMessage
    {
        public BidirectionalTestMessage(string name) => MessageName = name;

        public string MessageName { get; }
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public AnomalyMessageDirection Direction => AnomalyMessageDirection.Bidirectional;
        public AnomalyMessageAuthPhase AuthPhase => AnomalyMessageAuthPhase.AuthenticatedSession;
        public int MaxPayloadBytes => 0;
        public void Serialize(BinaryWriter writer) { }
        public void Deserialize(BinaryReader reader) { }
    }

    private sealed class TestClientMessage : AuthenticatedClientToServerMessage, IAnomalyMessage
    {
        public TestClientMessage(string name) => MessageName = name;
        public string MessageName { get; }
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public override int MaxPayloadBytes => 0;
        public void Serialize(BinaryWriter writer) { }
        public void Deserialize(BinaryReader reader) { }
    }

    private sealed class TestServerMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public TestServerMessage(string name) => MessageName = name;
        public string MessageName { get; }
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public override int MaxPayloadBytes => 0;
        public void Serialize(BinaryWriter writer) { }
        public void Deserialize(BinaryReader reader) { }
    }
}
