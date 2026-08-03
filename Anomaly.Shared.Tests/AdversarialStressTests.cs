using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using Anomaly.Client.FileTransfer;
using Anomaly.Server.Auth;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Utils;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class AdversarialStressTests : IDisposable
{
    private const int FloodRequestCount = 100_000;

    [Fact]
    public void UnauthenticatedPhaseOneFloodKeepsRatePendingAndLogStateBounded()
    {
        long now = 1_700_000_000;
        var handshake = ValidHandshake();

        AuthHandler.Reset();
        PreauthGate.InitializeForTests(() => now);
        OffThreadLogger.Disable();
        long droppedBefore = OffThreadLogger.DroppedCount;
        var elapsed = Stopwatch.StartNew();
        int accepted = 0;

        try
        {
            for (int i = 0; i < FloodRequestCount; i++)
            {
                IPAddress address = UniqueSubnetAddress(i);
                OffThreadLogger.Info("Connection from " + address);
                if (!PreauthGate.TryAcceptRequest(address))
                    continue;

                accepted++;
                byte[] cookie = PreauthGate.CreateRetryCookie(address, handshake);
                Assert.Equal(ClientHandshake.RetryCookieSize, cookie.Length);
            }
            elapsed.Stop();

            Assert.Equal(256, accepted);
            Assert.Equal(0, AuthHandler.PendingConnectionCount);
            Assert.Equal(4_096, PrivateCollectionCount(typeof(PreauthGate), "AddressBuckets"));
            Assert.Equal(1_024, PrivateCollectionCount(typeof(PreauthGate), "SubnetBuckets"));
            Assert.InRange(OffThreadLogger.QueuedCount, 0, 2_048);
            Assert.True(
                OffThreadLogger.DroppedCount - droppedBefore >= FloodRequestCount - 2_048,
                "The bounded logger did not account for all entries beyond its queue ceiling.");
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30),
                $"The deterministic 100,000-request flood took {elapsed.Elapsed}.");

            now++;
            IPAddress legitimatePeer = UniqueSubnetAddress(0);
            long[] latencyTicks = new long[128];
            for (int i = 0; i < latencyTicks.Length; i++)
            {
                long started = Stopwatch.GetTimestamp();
                PreauthGate.TryAcceptRequest(legitimatePeer);
                latencyTicks[i] = Stopwatch.GetTimestamp() - started;
            }
            Array.Sort(latencyTicks);
            double p99Milliseconds = latencyTicks[126] * 1_000d / Stopwatch.Frequency;
            Assert.True(p99Milliseconds < 250d,
                $"Post-flood preauth p99 was {p99Milliseconds:F3} ms.");
        }
        finally
        {
            OffThreadLogger.Disable();
            PreauthGate.Reset();
            AuthHandler.Reset();
        }
    }

    [Fact]
    public void PendingHandshakeFloodStopsAtGlobalAndPerAddressCaps()
    {
        AuthHandler.Reset();
        var handshake = ValidHandshake();
        var challenge = PasswordChallenge.NotRequired();
        int accepted = 0;

        try
        {
            for (int i = 0; i < FloodRequestCount; i++)
            {
                if (AuthHandler.TryReserveConnection(
                        handshake,
                        UniqueAddress(i),
                        challenge,
                        out _,
                        out _))
                    accepted++;
            }

            Assert.Equal(2_048, accepted);
            Assert.Equal(2_048, AuthHandler.PendingConnectionCount);
            Assert.Equal(2_048, PrivateCollectionCount(typeof(AuthHandler), "PendingByAddress"));
            Assert.Equal(2_048, PrivateCollectionCount(typeof(AuthHandler), "ExpiryOrder"));
        }
        finally
        {
            AuthHandler.Reset();
        }

        IPAddress oneAddress = IPAddress.Parse("192.0.2.10");
        for (int i = 0; i < 32; i++)
        {
            bool reserved = AuthHandler.TryReserveConnection(
                handshake,
                oneAddress,
                challenge,
                out _,
                out _);
            Assert.Equal(i < 8, reserved);
        }
        Assert.Equal(8, AuthHandler.PendingConnectionCount);
        AuthHandler.Reset();
    }

    [Fact]
    public void WrongEndpointCannotConsumeBoundPhaseTwoState()
    {
        AuthHandler.Reset();
        IPAddress boundAddress = IPAddress.Parse("192.0.2.10");
        Assert.True(AuthHandler.TryReserveConnection(
            ValidHandshake(),
            boundAddress,
            PasswordChallenge.NotRequired(),
            out string identifier,
            out _));

        Assert.False(AuthHandler.TryConsumeConnection(
            identifier,
            IPAddress.Parse("192.0.2.11"),
            out _));
        Assert.Equal(1, AuthHandler.PendingConnectionCount);

        Assert.True(AuthHandler.TryConsumeConnection(identifier, boundAddress, out _));
        Assert.Equal(0, AuthHandler.PendingConnectionCount);
        Assert.False(AuthHandler.TryConsumeConnection(identifier, boundAddress, out _));
    }

    [Fact]
    public void ServerTransferIgnoresUnsolicitedAndDuplicateAcknowledgements()
    {
        byte[] bytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        byte[] hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            hash = sha.ComputeHash(bytes);

        ServerAssetManager.SetAssets(new ServerAssetManager.AssetInfo
        {
            FullPath = path,
            RelativePath = "audio/test.ogg",
            Hash = hash,
            Size = (uint)bytes.Length,
            Kind = AssetKind.AudioClip,
        });
        var hub = new ReferenceHub();
        int completions = 0;
        Action<ReferenceHub> completed = _ => completions++;
        FileTransferSession.SessionCompleted += completed;

        try
        {
            FileTransferSession.StartForPlayer(hub);
            FileManifestMessage manifest = Assert.IsType<FileManifestMessage>(
                Anomaly.Server.Networking.AnomalyMessaging.Sent.Single());

            FileTransferSession.HandleMessage(hub, new FileAckMessage
            {
                SessionId = manifest.SessionId,
                Hash = hash,
                Status = 0,
            });
            Assert.Equal(1, ActiveTransferCount());
            Assert.Equal(0, completions);

            FileTransferSession.HandleMessage(hub, new FileRequestMessage
            {
                SessionId = manifest.SessionId,
                RequestedHashes = { hash },
            });
            Assert.Equal(1, ReservedTransferCount());

            object session = ActiveTransferFor(hub);
            SetTransferState(session, "AwaitingAck");
            FileTransferSession.HandleMessage(hub, new FileAckMessage
            {
                SessionId = manifest.SessionId,
                Hash = hash,
                Status = 0,
            });
            FileTransferSession.HandleMessage(hub, new FileAckMessage
            {
                SessionId = manifest.SessionId,
                Hash = hash,
                Status = 0,
            });

            Assert.Equal(0, ActiveTransferCount());
            Assert.Equal(0, ReservedTransferCount());
            Assert.Equal(1, completions);
        }
        finally
        {
            FileTransferSession.SessionCompleted -= completed;
            FileTransferSession.Reset();
            Anomaly.Server.Networking.AnomalyMessaging.ResetForTests();
            ServerAssetManager.SetAssets();
            File.Delete(path);
        }
    }

    [Fact]
    public void InterruptedServerAndClientTransfersReleaseAllStateAndPartialFiles()
    {
        byte[] data = new byte[FileChunkMessage.MaxChunkBytes + 17];
        byte[] hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            hash = sha.ComputeHash(data);
        string source = Path.GetTempFileName();
        File.WriteAllBytes(source, data);
        ServerAssetManager.SetAssets(new ServerAssetManager.AssetInfo
        {
            FullPath = source,
            RelativePath = "audio/interrupted.ogg",
            Hash = hash,
            Size = (uint)data.Length,
            Kind = AssetKind.AudioClip,
        });
        var hub = new ReferenceHub();

        try
        {
            FileTransferSession.StartForPlayer(hub);
            var manifest = Assert.IsType<FileManifestMessage>(
                Anomaly.Server.Networking.AnomalyMessaging.Sent.Single());
            FileTransferSession.HandleMessage(hub, new FileRequestMessage
            {
                SessionId = manifest.SessionId,
                RequestedHashes = { hash },
            });
            Assert.Equal(1, ReservedTransferCount());
            FileTransferSession.CleanupPlayer(hub);
            Assert.Equal(0, ActiveTransferCount());
            Assert.Equal(0, ReservedTransferCount());
            Assert.Equal(0L, ReservedTransferBytes());

            using (var buffer = new ChunkBuffer(77, hash, (uint)data.Length, "audio/interrupted.ogg"))
            {
                Assert.True(buffer.TryAddChunk(
                    0,
                    buffer.TotalChunks,
                    data.Take(FileChunkMessage.MaxChunkBytes).ToArray(),
                    out _,
                    out _));
                Assert.True(File.Exists(buffer.TempPath));
                string partial = buffer.TempPath;
                buffer.Dispose();
                Assert.False(File.Exists(partial));
            }
        }
        finally
        {
            FileTransferSession.Reset();
            Anomaly.Server.Networking.AnomalyMessaging.ResetForTests();
            ServerAssetManager.SetAssets();
            File.Delete(source);
        }
    }

    [Fact]
    public void ClientTransferRejectsUnsolicitedDuplicateOutOfOrderAndMalformedChunks()
    {
        byte[] complete = Enumerable.Range(0, FileChunkMessage.MaxChunkBytes + 17)
            .Select(i => (byte)i)
            .ToArray();
        byte[] hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            hash = sha.ComputeHash(complete);

        FileTransferClient.Reset();
        Anomaly.Client.Api.Networking.AnomalyMessaging.ResetForTests();
        var manifest = new FileManifestMessage
        {
            SessionId = 99,
            Files =
            {
                new FileManifestMessage.FileEntry(
                    "audio/chunks.ogg",
                    hash,
                    (uint)complete.Length,
                    AssetKind.AudioClip),
            },
        };
        InvokeClientTransfer("HandleManifest", manifest);
        Assert.Single(Anomaly.Client.Api.Networking.AnomalyMessaging.Sent);

        byte[] unsolicitedHash = Enumerable.Repeat((byte)0xA5, 32).ToArray();
        InvokeClientTransfer("HandleChunk", new FileChunkMessage
        {
            SessionId = 99,
            Hash = unsolicitedHash,
            ChunkIndex = 0,
            TotalChunks = 1,
            Data = new byte[1],
        });
        Assert.Equal(0, ClientTransferBufferCount());
        Assert.Equal(0L, ClientTransferReceivedBytes());

        byte[] first = complete.Take(FileChunkMessage.MaxChunkBytes).ToArray();
        var outOfOrderFinal = new FileChunkMessage
        {
            SessionId = 99,
            Hash = hash,
            ChunkIndex = 1,
            TotalChunks = 2,
            Data = complete.Skip(FileChunkMessage.MaxChunkBytes).ToArray(),
        };
        InvokeClientTransfer("HandleChunk", outOfOrderFinal);
        Assert.Equal(1, ClientTransferBufferCount());
        Assert.Equal(17L, ClientTransferReceivedBytes());

        InvokeClientTransfer("HandleChunk", outOfOrderFinal);
        Assert.Equal(17L, ClientTransferReceivedBytes());

        InvokeClientTransfer("HandleChunk", new FileChunkMessage
        {
            SessionId = 99,
            Hash = hash,
            ChunkIndex = 0,
            TotalChunks = 2,
            Data = first,
        });
        Assert.Equal(complete.Length, ClientTransferReceivedBytes());

        string partial = ClientTransferPartialPaths().Single();
        Assert.True(File.Exists(partial));
        InvokeClientTransfer("HandleChunk", new FileChunkMessage
        {
            SessionId = 99,
            Hash = hash,
            ChunkIndex = 0,
            TotalChunks = 3,
            Data = first,
        });
        Assert.Equal(complete.Length, ClientTransferReceivedBytes());
        Assert.Equal(0, ClientTransferBufferCount());
        Assert.Equal("Aborted", ClientTransferState());
        Assert.False(File.Exists(partial));

        FileTransferClient.Reset();
        Assert.Equal(0, ClientTransferBufferCount());
        Assert.Equal(0L, ClientTransferReceivedBytes());
        Assert.False(File.Exists(partial));
    }

    [Fact]
    public void ClientTransferDefersEarlyCompletionUntilReliableUnorderedChunksArrive()
    {
        byte[] complete = Enumerable.Range(0, 37).Select(i => (byte)i).ToArray();
        byte[] hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            hash = sha.ComputeHash(complete);

        FileTransferClient.Reset();
        Anomaly.Client.Api.Networking.AnomalyMessaging.ResetForTests();
        InvokeClientTransfer("HandleManifest", new FileManifestMessage
        {
            SessionId = 123,
            Files =
            {
                new FileManifestMessage.FileEntry(
                    "audio/early-complete.ogg",
                    hash,
                    (uint)complete.Length,
                    AssetKind.AudioClip),
            },
        });

        InvokeClientTransfer("HandleFileComplete", new FileTransferCompleteMessage
        {
            SessionId = 123,
            Hash = hash,
        });
        Assert.Equal("Receiving", ClientTransferState());
        Assert.Single(Anomaly.Client.Api.Networking.AnomalyMessaging.Sent);

        InvokeClientTransfer("HandleChunk", new FileChunkMessage
        {
            SessionId = 123,
            Hash = hash,
            ChunkIndex = 0,
            TotalChunks = 1,
            Data = complete,
        });

        Assert.Equal("Complete", ClientTransferState());
        Assert.Equal(0, ClientTransferBufferCount());
        Assert.Equal(2, Anomaly.Client.Api.Networking.AnomalyMessaging.Sent.Count);
        var ack = Assert.IsType<FileAckMessage>(
            Anomaly.Client.Api.Networking.AnomalyMessaging.Sent.Last());
        Assert.Equal(123ul, ack.SessionId);
        Assert.Equal((byte)0, ack.Status);
        Assert.Equal(hash, ack.Hash);
        FileTransferClient.Reset();
    }

    [Fact]
    public void TransferDecodersRejectOversizeAndSurviveDeterministicMalformedCorpus()
    {
        Assert.Throws<InvalidDataException>(() => Deserialize(
            new FileChunkMessage(),
            OversizeChunkPayload()));

        Assert.Throws<InvalidDataException>(() => Deserialize(
            new FileManifestMessage(),
            HeaderWithCount(1, FileManifestMessage.MaxFiles + 1)));

        Assert.Throws<InvalidDataException>(() => Deserialize(
            new FileRequestMessage(),
            HeaderWithCount(1, FileRequestMessage.MaxRequestedHashes + 1)));

        var random = new Random(0x5A17);
        Func<IAnomalyMessage>[] factories =
        {
            () => new FileManifestMessage(),
            () => new FileRequestMessage(),
            () => new FileChunkMessage(),
            () => new FileTransferCompleteMessage(),
            () => new FileAckMessage(),
        };

        var elapsed = Stopwatch.StartNew();
        for (int i = 0; i < 20_000; i++)
        {
            byte[] payload = new byte[random.Next(0, 1_025)];
            random.NextBytes(payload);
            IAnomalyMessage message = factories[i % factories.Length]();
            try
            {
                Deserialize(message, payload);
                byte[] canonical = Serialize(message);
                Assert.InRange(canonical.Length, 0, ((IBoundedAnomalyMessage)message).MaxPayloadBytes);
            }
            catch (Exception ex) when (IsExpectedDecoderRejection(ex))
            {
                // Expected fail-closed result for malformed input.
            }
        }
        elapsed.Stop();
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(20),
            $"The deterministic malformed corpus took {elapsed.Elapsed}.");
    }

    public void Dispose()
    {
        FileTransferSession.Reset();
        Anomaly.Server.Networking.AnomalyMessaging.ResetForTests();
        Anomaly.Client.Api.Networking.AnomalyMessaging.ResetForTests();
        ServerAssetManager.SetAssets();
        OffThreadLogger.Disable();
        PreauthGate.Reset();
        AuthHandler.Reset();

        string root = Anomaly.Client.Api.Features.Paths.FileCachePath;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    private static ClientHandshake ValidHandshake() => new ClientHandshake
    {
        identityPublic = Enumerable.Repeat((byte)0x04, ClientHandshake.IdentityPublicSize).ToArray(),
        sessionPublic = Enumerable.Repeat((byte)0x31, ClientHandshake.SessionPublicSize).ToArray(),
        nonce = Enumerable.Repeat((byte)0x52, ClientHandshake.NonceSize).ToArray(),
        gameMajor = 14,
        gameMinor = 2,
        gameRevision = 7,
        gameBackwardCompatibility = false,
        gameBackwardRevision = 0,
        connectionContext = AuthConnectionContext.NormalizeEndpoint("example.test", 7777),
        retryCookie = Array.Empty<byte>(),
    };

    private static IPAddress UniqueSubnetAddress(int value) => new IPAddress(new byte[]
    {
        (byte)(1 + ((value >> 16) % 223)),
        (byte)(value >> 8),
        (byte)value,
        1,
    });

    private static IPAddress UniqueAddress(int value) => new IPAddress(new byte[]
    {
        198,
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value,
    });

    private static int PrivateCollectionCount(Type owner, string fieldName)
    {
        object value = owner.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) ?? throw new InvalidOperationException($"Missing {owner.Name}.{fieldName}.");
        return value switch
        {
            ICollection collection => collection.Count,
            _ => (int)(value.GetType().GetProperty("Count")?.GetValue(value)
                ?? throw new InvalidOperationException($"{fieldName} has no Count.")),
        };
    }

    private static int ActiveTransferCount()
        => PrivateCollectionCount(typeof(FileTransferSession), "ActiveSessions");

    private static object ActiveTransferFor(ReferenceHub hub)
    {
        object sessions = typeof(FileTransferSession)
            .GetField("ActiveSessions", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) ?? throw new InvalidOperationException("Missing active transfer map.");
        return sessions.GetType().GetProperty("Item")?.GetValue(sessions, new object[] { hub })
            ?? throw new InvalidOperationException("Missing transfer session.");
    }

    private static void SetTransferState(object session, string name)
    {
        FieldInfo field = session.GetType().GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Missing transfer state field.");
        field.SetValue(session, Enum.Parse(field.FieldType, name));
    }

    private static int ReservedTransferCount()
        => (int)(typeof(FileTransferSession)
            .GetField("_reservedTransferCount", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) ?? -1);

    private static long ReservedTransferBytes()
        => (long)(typeof(FileTransferSession)
            .GetField("_globalReservedBytes", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) ?? -1L);

    private static void InvokeClientTransfer(string methodName, IAnomalyMessage message)
    {
        MethodInfo method = typeof(FileTransferClient).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing FileTransferClient.{methodName}.");
        try
        {
            method.Invoke(null, new object[] { message });
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw exception.InnerException;
        }
    }

    private static int ClientTransferBufferCount()
        => PrivateCollectionCount(typeof(FileTransferClient), "_buffers");

    private static long ClientTransferReceivedBytes()
        => (long)(typeof(FileTransferClient)
            .GetField("_receivedBytes", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) ?? -1L);

    private static string ClientTransferState()
        => typeof(FileTransferClient)
            .GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null)?.ToString() ?? "<missing>";

    private static IEnumerable<string> ClientTransferPartialPaths()
    {
        object buffers = typeof(FileTransferClient)
            .GetField("_buffers", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) ?? throw new InvalidOperationException("Missing client transfer buffers.");
        foreach (object value in (IEnumerable)((IDictionary)buffers).Values)
            yield return ((ChunkBuffer)value).TempPath;
    }

    private static byte[] OversizeChunkPayload()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1ul);
        writer.Write(new byte[32]);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)(FileChunkMessage.MaxChunkBytes + 1));
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] HeaderWithCount(ulong sessionId, int count)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(sessionId);
        writer.Write((ushort)count);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Deserialize(IAnomalyMessage message, byte[] payload)
    {
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream);
        message.Deserialize(reader);
        Assert.InRange(stream.Position, 0, stream.Length);
    }

    private static byte[] Serialize(IAnomalyMessage message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        message.Serialize(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static bool IsExpectedDecoderRejection(Exception exception)
        => exception is InvalidDataException
            || exception is EndOfStreamException
            || exception is ArgumentException
            || exception is IOException
            || exception is OverflowException;
}
