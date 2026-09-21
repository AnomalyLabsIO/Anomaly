using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Anomaly.Client.FileTransfer;
using Anomaly.Server.Auth;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Server.Utils;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Content;
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
    public void TeardownDrainRemovesAcceptedPendingHandoffsAndClearsSecrets()
    {
        AuthHandler.Reset();
        var endpoint = new IPEndPoint(IPAddress.Parse("192.0.2.42"), 7777);
        var player = new PlayerStorage
        {
            UserId = "pending@anomaly",
            ClientToServerKey = Enumerable.Repeat((byte)0x11, 32).ToArray(),
            ServerToClientKey = Enumerable.Repeat((byte)0x22, 32).ToArray(),
            SessionId = Enumerable.Repeat((byte)0x33, AuthTransportCrypto.SessionIdSize).ToArray(),
            TranscriptHash = Enumerable.Repeat((byte)0x44, AuthTransportCrypto.TranscriptHashSize).ToArray(),
        };

        Assert.True(AuthHandler.StorePlayer(endpoint, player));
        Assert.Equal(1, AuthHandler.StoredPlayerCount);
        Assert.Equal(1, AuthHandler.DrainStoredPlayers());
        Assert.Equal(0, AuthHandler.StoredPlayerCount);
        Assert.False(AuthHandler.TryTakePlayer(endpoint, out _));
        Assert.Null(player.ClientToServerKey);
        Assert.Null(player.ServerToClientKey);
        Assert.Null(player.SessionId);
        Assert.Null(player.TranscriptHash);
    }

    [Fact]
    public async Task TransportQuarantineTransitionIsNeverInvisibleToTeardownSnapshot()
    {
        var hub = new ReferenceHub { netId = 42 };
        AuthenticatedTransportSessions.Remove(hub);
        AuthenticatedTransportSessions.Register(hub, ValidTransportPlayer());

        int missingSnapshots = 0;
        using (var start = new ManualResetEventSlim(false))
        {
            Task mutator = Task.Run(() =>
            {
                start.Wait();
                for (int index = 0; index < 10_000; index++)
                {
                    AuthenticatedTransportSessions.Quarantine(hub);
                    AuthenticatedTransportSessions.Register(hub, ValidTransportPlayer());
                }
            });
            Task observer = Task.Run(() =>
            {
                start.Wait();
                while (!mutator.IsCompleted)
                {
                    if (!AuthenticatedTransportSessions.SnapshotHubs().Contains(hub))
                        Interlocked.Increment(ref missingSnapshots);
                }
            });

            start.Set();
            await Task.WhenAll(mutator, observer);
        }

        try
        {
            Assert.Equal(0, missingSnapshots);
            Assert.Contains(hub, AuthenticatedTransportSessions.SnapshotHubs());
        }
        finally
        {
            AuthenticatedTransportSessions.Remove(hub);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_only_content_registration_cannot_be_offered_as_an_empty_catalog(bool registerNull)
    {
        ContentRegistry.Reset();
        try
        {
            ContentRegistry.Add(registerNull
                ? null
                : new CustomItemDefinition { Key = "INVALID KEY" });

            Assert.Throws<InvalidOperationException>(() =>
                ContentRegistry.CreateOfferCatalog(new byte[32]));
            Assert.False(ContentRegistry.Materialized);
        }
        finally
        {
            ContentRegistry.Reset();
        }
    }

    [Fact]
    public void Deliberately_empty_content_catalog_freezes_and_offers_successfully()
    {
        ContentRegistry.Reset();
        try
        {
            ContentCatalog offer = ContentRegistry.CreateOfferCatalog(
                FileManifestMessage.ComputeManifestSha256(Array.Empty<FileManifestMessage.FileEntry>()));

            Assert.True(ContentRegistry.Materialized);
            Assert.Empty(offer.Entries);
        }
        finally
        {
            ContentRegistry.Reset();
        }
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
            ApproveServerTransfer(hub, manifest);

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
            Assert.True(FileTransferSession.IsAssetApproved(hub, hash, AssetKind.AudioClip));
            Assert.False(FileTransferSession.IsAssetApproved(hub, hash, AssetKind.Texture2D));

            FileTransferSession.CleanupPlayer(hub);
            Assert.False(FileTransferSession.IsAssetApproved(hub, hash, AssetKind.AudioClip));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServerCompletionMarkerSendFailureAbortsAndReleasesBudget(bool throwOnSend)
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
            RelativePath = "audio/completion-failure.ogg",
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
            var manifest = Assert.IsType<FileManifestMessage>(
                Anomaly.Server.Networking.AnomalyMessaging.Sent.Single());
            ApproveServerTransfer(hub, manifest);
            FileTransferSession.HandleMessage(hub, new FileRequestMessage
            {
                SessionId = manifest.SessionId,
                RequestedHashes = { hash },
            });
            Assert.Equal(1, ReservedTransferCount());

            if (throwOnSend)
            {
                Anomaly.Server.Networking.AnomalyMessaging.TrySendException = message =>
                    message is FileTransferCompleteMessage
                        ? new InvalidOperationException("injected completion send failure")
                        : null;
            }
            else
            {
                Anomaly.Server.Networking.AnomalyMessaging.TrySendResult =
                    message => !(message is FileTransferCompleteMessage);
            }

            DrainCurrentServerFile(ActiveTransferFor(hub));

            Assert.Equal(0, ActiveTransferCount());
            Assert.Equal(0, ReservedTransferCount());
            Assert.Equal(0L, ReservedTransferBytes());
            Assert.Equal(0, completions);
            Assert.Equal(1, Anomaly.Server.Plugin.QuarantineCount);
            Assert.Same(hub, Anomaly.Server.Plugin.LastQuarantinedHub);
            Assert.False(hub.connectionToClient.isReady);
            Assert.Equal(ContentReadiness.Unknown, ContentGrantGate.GetReadiness(hub.netId));
            Assert.DoesNotContain(
                Anomaly.Server.Networking.AnomalyMessaging.Sent,
                message => message is FileTransferCompleteMessage);
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
    public void ServerTransferReleasesLogicalOwnershipBeforeCoroutineCancellationFailures()
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
            RelativePath = "audio/cancellation-failure.ogg",
            Hash = hash,
            Size = (uint)bytes.Length,
            Kind = AssetKind.AudioClip,
        });
        var hub = new ReferenceHub();

        try
        {
            FileTransferSession.StartForPlayer(hub);
            var manifest = Assert.IsType<FileManifestMessage>(
                Anomaly.Server.Networking.AnomalyMessaging.Sent.Single());
            ApproveServerTransfer(hub, manifest);
            FileTransferSession.HandleMessage(hub, new FileRequestMessage
            {
                SessionId = manifest.SessionId,
                RequestedHashes = { hash },
            });
            Assert.Equal(1, ActiveTransferCount());
            Assert.Equal(1, ReservedTransferCount());

            MEC.Timing.ThrowOnKill = true;
            FileTransferSession.CleanupPlayer(hub);

            Assert.Equal(0, ActiveTransferCount());
            Assert.Equal(0, ReservedTransferCount());
            Assert.Equal(0L, ReservedTransferBytes());
            Assert.False(FileTransferSession.IsAssetApproved(hub, hash, AssetKind.AudioClip));
            Assert.Equal(0, Anomaly.Server.Plugin.QuarantineCount);
            Assert.True(hub.connectionToClient.isReady);
        }
        finally
        {
            MEC.Timing.ThrowOnKill = false;
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
            ApproveServerTransfer(hub, manifest);
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
    public void ServerTransferCoroutineStartFailureRevokesAdmissionAfterReleasingState()
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
            RelativePath = "audio/coroutine-start-failure.ogg",
            Hash = hash,
            Size = (uint)bytes.Length,
            Kind = AssetKind.AudioClip,
        });
        var hub = new ReferenceHub();

        try
        {
            FileTransferSession.StartForPlayer(hub);
            var manifest = Assert.IsType<FileManifestMessage>(
                Anomaly.Server.Networking.AnomalyMessaging.Sent.Single());
            ApproveServerTransfer(hub, manifest);
            MEC.Timing.ThrowOnRun = true;

            FileTransferSession.HandleMessage(hub, new FileRequestMessage
            {
                SessionId = manifest.SessionId,
                RequestedHashes = { hash },
            });

            Assert.Equal(0, ActiveTransferCount());
            Assert.Equal(0, ReservedTransferCount());
            Assert.Equal(0L, ReservedTransferBytes());
            Assert.Equal(ContentReadiness.Unknown, ContentGrantGate.GetReadiness(hub.netId));
            Assert.Equal(1, Anomaly.Server.Plugin.QuarantineCount);
            Assert.Same(hub, Anomaly.Server.Plugin.LastQuarantinedHub);
            Assert.False(hub.connectionToClient.isReady);
        }
        finally
        {
            MEC.Timing.ThrowOnRun = false;
            FileTransferSession.Reset();
            Anomaly.Server.Networking.AnomalyMessaging.ResetForTests();
            ServerAssetManager.SetAssets();
            File.Delete(path);
        }
    }

    [Fact]
    public void ServerTransferRetryCancellationFailureRevokesAdmissionAfterReleasingState()
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
            RelativePath = "audio/retry-cancellation-failure.ogg",
            Hash = hash,
            Size = (uint)bytes.Length,
            Kind = AssetKind.AudioClip,
        });
        var hub = new ReferenceHub();

        try
        {
            FileTransferSession.StartForPlayer(hub);
            var manifest = Assert.IsType<FileManifestMessage>(
                Anomaly.Server.Networking.AnomalyMessaging.Sent.Single());
            ApproveServerTransfer(hub, manifest);
            FileTransferSession.HandleMessage(hub, new FileRequestMessage
            {
                SessionId = manifest.SessionId,
                RequestedHashes = { hash },
            });
            Assert.Equal(1, ReservedTransferCount());

            SetTransferState(ActiveTransferFor(hub), "AwaitingAck");
            MEC.Timing.ThrowOnKill = true;
            FileTransferSession.HandleMessage(hub, new FileAckMessage
            {
                SessionId = manifest.SessionId,
                Hash = hash,
                Status = 1,
            });

            Assert.Equal(0, ActiveTransferCount());
            Assert.Equal(0, ReservedTransferCount());
            Assert.Equal(0L, ReservedTransferBytes());
            Assert.Equal(ContentReadiness.Unknown, ContentGrantGate.GetReadiness(hub.netId));
            Assert.Equal(1, Anomaly.Server.Plugin.QuarantineCount);
            Assert.Same(hub, Anomaly.Server.Plugin.LastQuarantinedHub);
            Assert.False(hub.connectionToClient.isReady);
        }
        finally
        {
            MEC.Timing.ThrowOnKill = false;
            FileTransferSession.Reset();
            Anomaly.Server.Networking.AnomalyMessaging.ResetForTests();
            ServerAssetManager.SetAssets();
            File.Delete(path);
        }
    }

    [Fact]
    public void ServerTransferCompletionStateMismatchRevokesAdmission()
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
            RelativePath = "audio/completion-state-mismatch.ogg",
            Hash = hash,
            Size = (uint)bytes.Length,
            Kind = AssetKind.AudioClip,
        });
        var hub = new ReferenceHub();

        try
        {
            FileTransferSession.StartForPlayer(hub);
            var manifest = Assert.IsType<FileManifestMessage>(
                Anomaly.Server.Networking.AnomalyMessaging.Sent.Single());
            ApproveServerTransfer(hub, manifest);
            FileTransferSession.HandleMessage(hub, new FileRequestMessage
            {
                SessionId = manifest.SessionId,
                RequestedHashes = { hash },
            });
            Assert.Equal(1, ReservedTransferCount());

            object session = ActiveTransferFor(hub);
            SetTransferState(session, "AwaitingAck");
            ContentGrantGate.Remove(hub.netId);
            FileTransferSession.HandleMessage(hub, new FileAckMessage
            {
                SessionId = manifest.SessionId,
                Hash = hash,
                Status = 0,
            });

            Assert.Equal(0, ActiveTransferCount());
            Assert.Equal(0, ReservedTransferCount());
            Assert.Equal(0L, ReservedTransferBytes());
            Assert.Equal(1, Anomaly.Server.Plugin.QuarantineCount);
            Assert.Same(hub, Anomaly.Server.Plugin.LastQuarantinedHub);
            Assert.False(hub.connectionToClient.isReady);
            Assert.False(FileTransferSession.IsAssetApproved(hub, hash, AssetKind.AudioClip));
        }
        finally
        {
            FileTransferSession.Reset();
            Anomaly.Server.Networking.AnomalyMessaging.ResetForTests();
            ServerAssetManager.SetAssets();
            File.Delete(path);
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
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
        InvokeClientTransfer("HandleManifest", manifest);
        ApproveClientTransfer(manifest.ManifestSha256);
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
    public void ClientTransferAbortSignalsFailureBeforeLoggerUiAndPublicEventFailures()
    {
        FileTransferClient.Reset();
        bool failureObserved = false;
        Action<bool> handler = successful => failureObserved = !successful;
        FileTransferClient.TransferFinished += handler;
        Anomaly.Client.Api.Events.Handlers.FileTransferEvents.ThrowOnComplete = true;
        Anomaly.Client.Components.FileTransferOverlay.ThrowOnHide = true;
        MelonLoader.MelonLogger.ThrowOnError = true;

        try
        {
            MethodInfo abort = typeof(FileTransferClient).GetMethod(
                "Abort",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Missing FileTransferClient.Abort.");
            abort.Invoke(null, new object[] { "injected failure" });

            Assert.True(failureObserved);
            Assert.Equal("Aborted", ClientTransferState());
        }
        finally
        {
            FileTransferClient.TransferFinished -= handler;
            Anomaly.Client.Api.Events.Handlers.FileTransferEvents.ThrowOnComplete = false;
            Anomaly.Client.Components.FileTransferOverlay.ThrowOnHide = false;
            MelonLoader.MelonLogger.ThrowOnError = false;
            FileTransferClient.Reset();
        }
    }

    [Fact]
    public void ClientConsentDeclineSendsNoFileRequestAndAuthorizesNoBytes()
    {
        byte[] hash = Enumerable.Repeat((byte)0x6D, 32).ToArray();
        var manifest = new FileManifestMessage
        {
            SessionId = 126,
            Files =
            {
                new FileManifestMessage.FileEntry(
                    "textures/declined.png",
                    hash,
                    4096,
                    AssetKind.Texture2D),
            },
        };
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);

        FileTransferClient.Reset();
        Anomaly.Client.Api.Networking.AnomalyMessaging.ResetForTests();
        try
        {
            InvokeClientTransfer("HandleManifest", manifest);
            Assert.Equal("AwaitingConsent", ClientTransferState());

            FileTransferClient.DeclinePendingManifest(manifest.ManifestSha256);

            Assert.Equal("Aborted", ClientTransferState());
            Assert.Empty(Anomaly.Client.Api.Networking.AnomalyMessaging.Sent);
            Assert.Equal(0L, ClientTransferRequestedBytes());
            Assert.Equal(0L, ClientTransferReceivedBytes());
            Assert.Equal(0, ClientTransferBufferCount());
            Assert.False(FileTransferClient.IsAssetApproved(hash, AssetKind.Texture2D));
        }
        finally
        {
            FileTransferClient.Reset();
        }
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
        var manifest = new FileManifestMessage
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
        };
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
        InvokeClientTransfer("HandleManifest", manifest);
        ApproveClientTransfer(manifest.ManifestSha256);

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
    public void ClientTransferCacheCommitExceptionFailsClosedAfterReleasingBuffer()
    {
        byte[] complete = Enumerable.Range(0, 37).Select(i => (byte)i).ToArray();
        byte[] hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            hash = sha.ComputeHash(complete);

        FileTransferClient.Reset();
        Anomaly.Client.Api.Networking.AnomalyMessaging.ResetForTests();
        bool failureObserved = false;
        Action<bool> handler = successful => failureObserved = !successful;
        FileTransferClient.TransferFinished += handler;
        Anomaly.Client.Api.FileTransfer.FileCache.ThrowOnImport = true;
        try
        {
            var manifest = new FileManifestMessage
            {
                SessionId = 124,
                Files =
                {
                    new FileManifestMessage.FileEntry(
                        "audio/cache-failure.ogg",
                        hash,
                        (uint)complete.Length,
                        AssetKind.AudioClip),
                },
            };
            manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
            InvokeClientTransfer("HandleManifest", manifest);
            ApproveClientTransfer(manifest.ManifestSha256);
            InvokeClientTransfer("HandleFileComplete", new FileTransferCompleteMessage
            {
                SessionId = 124,
                Hash = hash,
            });
            InvokeClientTransfer("HandleChunk", new FileChunkMessage
            {
                SessionId = 124,
                Hash = hash,
                ChunkIndex = 0,
                TotalChunks = 1,
                Data = complete,
            });

            Assert.True(failureObserved);
            Assert.Equal("Aborted", ClientTransferState());
            Assert.Equal(0, ClientTransferBufferCount());
            var failureAck = Assert.IsType<FileAckMessage>(
                Anomaly.Client.Api.Networking.AnomalyMessaging.Sent.Last());
            Assert.Equal((byte)1, failureAck.Status);
        }
        finally
        {
            Anomaly.Client.Api.FileTransfer.FileCache.ThrowOnImport = false;
            FileTransferClient.TransferFinished -= handler;
            FileTransferClient.Reset();
        }
    }

    [Fact]
    public void ClientTransferUnknownCompletionAcknowledgesBeforeThrowingWarningLogger()
    {
        byte[] advertisedHash = Enumerable.Repeat((byte)0x2A, 32).ToArray();
        byte[] unknownHash = Enumerable.Repeat((byte)0x5A, 32).ToArray();
        FileTransferClient.Reset();
        Anomaly.Client.Api.Networking.AnomalyMessaging.ResetForTests();
        MelonLoader.MelonLogger.ThrowOnWarning = true;
        try
        {
            var manifest = new FileManifestMessage
            {
                SessionId = 125,
                Files =
                {
                    new FileManifestMessage.FileEntry(
                        "audio/known.ogg",
                        advertisedHash,
                        1,
                        AssetKind.AudioClip),
                },
            };
            manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
            InvokeClientTransfer("HandleManifest", manifest);
            ApproveClientTransfer(manifest.ManifestSha256);
            InvokeClientTransfer("HandleFileComplete", new FileTransferCompleteMessage
            {
                SessionId = 125,
                Hash = unknownHash,
            });

            Assert.Equal("Receiving", ClientTransferState());
            var failureAck = Assert.IsType<FileAckMessage>(
                Anomaly.Client.Api.Networking.AnomalyMessaging.Sent.Last());
            Assert.Equal(unknownHash, failureAck.Hash);
            Assert.Equal((byte)1, failureAck.Status);
        }
        finally
        {
            MelonLoader.MelonLogger.ThrowOnWarning = false;
            FileTransferClient.Reset();
        }
    }

    [Fact]
    public void TransferDecodersRejectOversizeAndSurviveDeterministicMalformedCorpus()
    {
        Assert.Throws<InvalidDataException>(() => Deserialize(
            new FileChunkMessage(),
            OversizeChunkPayload()));

        Assert.Throws<InvalidDataException>(() => Deserialize(
            new FileManifestMessage(),
            ManifestHeaderWithCount(1, FileManifestMessage.MaxFiles + 1)));

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
        ContentGrantGate.Reset();
        Anomaly.Server.Plugin.ResetForTests();
        MEC.Timing.ThrowOnKill = false;
        MEC.Timing.ThrowOnRun = false;
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

    private static PlayerStorage ValidTransportPlayer() => new PlayerStorage
    {
        UserId = "transport@anomaly",
        ClientToServerKey = Enumerable.Repeat((byte)0x11, 32).ToArray(),
        ServerToClientKey = Enumerable.Repeat((byte)0x22, 32).ToArray(),
        SessionId = Enumerable.Repeat((byte)0x33, AuthTransportCrypto.SessionIdSize).ToArray(),
        TranscriptHash = Enumerable.Repeat((byte)0x44, AuthTransportCrypto.TranscriptHashSize).ToArray(),
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

    private static void DrainCurrentServerFile(object session)
    {
        FieldInfo currentFileField = session.GetType().GetField(
            "_currentFile",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Missing current server transfer file.");
        MethodInfo sendFileChunks = session.GetType().GetMethod(
            "SendFileChunks",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Missing server transfer coroutine.");
        var routine = (IEnumerator<float>)sendFileChunks.Invoke(
            session,
            new[] { currentFileField.GetValue(session) });
        while (routine.MoveNext())
        {
        }
    }

    private static void ApproveServerTransfer(ReferenceHub hub, FileManifestMessage manifest)
    {
        ContentCatalog catalog = CatalogBuilder.Build(
            Array.Empty<CatalogEntry>(),
            itemIdBase: 128,
            itemIdCeiling: 127,
            manifest.ManifestSha256);
        ContentGrantGate.RecordOffer(hub.netId, catalog);
        Assert.True(ContentGrantGate.TryRecordDecision(
            hub.netId,
            catalog.CatalogSha256,
            catalog.ManifestSha256,
            approved: true));
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

    private static void ApproveClientTransfer(byte[] manifestSha256)
    {
        MethodInfo method = typeof(FileTransferClient).GetMethod(
            "ApprovePendingManifest",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing FileTransferClient.ApprovePendingManifest.");
        Assert.True((bool)method.Invoke(null, new object[] { manifestSha256 }));
    }

    private static int ClientTransferBufferCount()
        => PrivateCollectionCount(typeof(FileTransferClient), "_buffers");

    private static long ClientTransferReceivedBytes()
        => (long)(typeof(FileTransferClient)
            .GetField("_receivedBytes", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) ?? -1L);

    private static long ClientTransferRequestedBytes()
        => (long)(typeof(FileTransferClient)
            .GetField("_totalBytes", BindingFlags.NonPublic | BindingFlags.Static)
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

    private static byte[] ManifestHeaderWithCount(ulong sessionId, int count)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(sessionId);
        writer.Write(new byte[32]);
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
