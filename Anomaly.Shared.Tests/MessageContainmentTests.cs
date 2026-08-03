using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class MessageContainmentTests
{
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

    [Fact]
    public void Manifest_round_trip_binds_session_hash_size_kind_and_safe_path()
    {
        byte[] hash = Enumerable.Repeat((byte)0xA5, 32).ToArray();
        var source = new FileManifestMessage
        {
            SessionId = 42,
            Files =
            {
                new FileManifestMessage.FileEntry("audio/word.ogg", hash, 1024, AssetKind.AudioClip)
            }
        };

        FileManifestMessage decoded = RoundTrip(source, () => new FileManifestMessage());

        Assert.Equal(42ul, decoded.SessionId);
        Assert.Single(decoded.Files);
        Assert.Equal("audio/word.ogg", decoded.Files[0].Path);
        Assert.Equal(hash, decoded.Files[0].Hash);
        Assert.Equal(1024u, decoded.Files[0].Size);
        Assert.Equal(AssetKind.AudioClip, decoded.Files[0].Kind);
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
                    AssetKind.AssetBundle)
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
    public void Content_catalog_rejects_role_activation_in_items_only_profile()
    {
        var message = new ContentCatalogMessage
        {
            Catalog = new ContentCatalog
            {
                Entries =
                {
                    new CatalogEntry
                    {
                        Key = "example:unsafe_role",
                        Kind = CatalogEntryKind.Role,
                        DisplayName = "Unsafe role"
                    }
                }
            }
        };

        Assert.Throws<InvalidDataException>(() => Serialize(message));
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
}
