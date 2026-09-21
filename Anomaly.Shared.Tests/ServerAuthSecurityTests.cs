using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Anomaly.Server.Auth;
using Anomaly.Server.Networking;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Xunit;

public sealed class ServerAuthSecurityTests
{
    [Fact]
    public void AcceptedIdentityJournal_PersistsAdvanceAndRejectsRollbackAndFork()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "accepted_client_identities.json");
        byte[] root = AuthCrypto.CreateIdentity();
        byte[] leafOne = AuthCrypto.CreateIdentity();
        byte[] forkOne = AuthCrypto.CreateIdentity();
        byte[] leafTwo = AuthCrypto.CreateIdentity();
        try
        {
            var rootHandshake = HandshakeFor(root, null);
            var chainOne = ClientIdentityRotationChain.Create(root, leafOne);
            var leafOneHandshake = HandshakeFor(leafOne, chainOne);
            var forkHandshake = HandshakeFor(
                forkOne,
                ClientIdentityRotationChain.Create(root, forkOne));
            var chainTwo = chainOne.Append(leafOne, leafTwo);
            var leafTwoHandshake = HandshakeFor(leafTwo, chainTwo);

            var journal = new AcceptedClientIdentityJournal(path);
            Assert.True(journal.TryLoad(out var loadError), loadError);
            Assert.True(journal.TryValidateAndCommit("enrollment@test", rootHandshake, out var seedError), seedError);
            Assert.True(journal.TryValidateAndCommit("enrollment@test", leafOneHandshake, out var advanceError), advanceError);
            Assert.True(File.Exists(path + ".bak"));

            var reloaded = new AcceptedClientIdentityJournal(path);
            Assert.True(reloaded.TryLoad(out loadError), loadError);
            Assert.True(reloaded.TryValidateAndCommit("enrollment@test", leafOneHandshake, out var reconnectError), reconnectError);
            Assert.False(reloaded.TryValidateAndCommit("enrollment@test", rootHandshake, out var rollbackError));
            Assert.Contains("rollback", rollbackError, StringComparison.OrdinalIgnoreCase);
            Assert.False(reloaded.TryValidateAndCommit("enrollment@test", forkHandshake, out var forkError));
            Assert.Contains("fork", forkError, StringComparison.OrdinalIgnoreCase);
            Assert.True(reloaded.TryValidateAndCommit("enrollment@test", leafTwoHandshake, out var secondAdvanceError), secondAdvanceError);
        }
        finally
        {
            Clear(root);
            Clear(leafOne);
            Clear(forkOne);
            Clear(leafTwo);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AcceptedIdentityJournal_CorruptionFailsClosed()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "accepted_client_identities.json");
        try
        {
            File.WriteAllText(path, "{ definitely-not-valid-json");
            var journal = new AcceptedClientIdentityJournal(path);
            Assert.False(journal.TryLoad(out var error));
            Assert.Contains("could not be loaded safely", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, journal.Count);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AcceptedIdentityJournal_MissingPrimaryWithBackupFailsClosed()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "accepted_client_identities.json");
        try
        {
            File.WriteAllText(path + ".bak", "{}");
            var journal = new AcceptedClientIdentityJournal(path);
            Assert.False(journal.TryLoad(out var error));
            Assert.Contains("backup exists", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AcceptedIdentityJournal_ConcurrentCompetingAdvancesCommitExactlyOneLeaf()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "accepted_client_identities.json");
        byte[] root = AuthCrypto.CreateIdentity();
        byte[] leafOne = AuthCrypto.CreateIdentity();
        byte[] leafTwoA = AuthCrypto.CreateIdentity();
        byte[] leafTwoB = AuthCrypto.CreateIdentity();
        try
        {
            var rootHandshake = HandshakeFor(root, null);
            var chainOne = ClientIdentityRotationChain.Create(root, leafOne);
            var leafOneHandshake = HandshakeFor(leafOne, chainOne);
            var handshakeA = HandshakeFor(leafTwoA, chainOne.Append(leafOne, leafTwoA));
            var handshakeB = HandshakeFor(leafTwoB, chainOne.Append(leafOne, leafTwoB));

            var journal = new AcceptedClientIdentityJournal(path);
            Assert.True(journal.TryLoad(out var loadError), loadError);
            Assert.True(journal.TryValidateAndCommit("race@test", rootHandshake, out var seedError), seedError);
            Assert.True(journal.TryValidateAndCommit("race@test", leafOneHandshake, out var advanceError), advanceError);

            var start = new ManualResetEventSlim(false);
            var accepted = new bool[2];
            var errors = new string[2];
            Task first = Task.Run(() =>
            {
                start.Wait();
                accepted[0] = journal.TryValidateAndCommit("race@test", handshakeA, out errors[0]);
            });
            Task second = Task.Run(() =>
            {
                start.Wait();
                accepted[1] = journal.TryValidateAndCommit("race@test", handshakeB, out errors[1]);
            });
            start.Set();
            await Task.WhenAll(first, second);

            Assert.NotEqual(accepted[0], accepted[1]);
            int rejected = accepted[0] ? 1 : 0;
            Assert.Contains("fork", errors[rejected], StringComparison.OrdinalIgnoreCase);

            var reloaded = new AcceptedClientIdentityJournal(path);
            Assert.True(reloaded.TryLoad(out loadError), loadError);
            Assert.True(reloaded.TryValidateAndCommit(
                "race@test",
                accepted[0] ? handshakeA : handshakeB,
                out var reconnectError), reconnectError);
            Assert.False(reloaded.TryValidateAndCommit(
                "race@test",
                accepted[0] ? handshakeB : handshakeA,
                out var persistedForkError));
            Assert.Contains("fork", persistedForkError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Clear(root);
            Clear(leafOne);
            Clear(leafTwoA);
            Clear(leafTwoB);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void EnrollmentSessionRegistry_NewerReservationWinsAndRemovalIsConditional()
    {
        EnrollmentSessionRegistry.Reset();
        try
        {
            var firstHub = new ReferenceHub();
            var secondHub = new ReferenceHub();
            ulong first = EnrollmentSessionRegistry.Reserve("same@test", false);
            ulong second = EnrollmentSessionRegistry.Reserve("same@test", false);

            Assert.False(EnrollmentSessionRegistry.TryActivate(
                "same@test", first, firstHub, false, out _));
            Assert.True(EnrollmentSessionRegistry.TryActivate(
                "same@test", second, firstHub, false, out var initialPrevious));
            Assert.Null(initialPrevious);

            ulong replacement = EnrollmentSessionRegistry.Reserve("same@test", false);
            Assert.True(EnrollmentSessionRegistry.TryActivate(
                "same@test", replacement, secondHub, false, out var previous));
            Assert.Same(firstHub, previous);

            EnrollmentSessionRegistry.Remove(firstHub);
            Assert.Equal(1, EnrollmentSessionRegistry.Count);
            EnrollmentSessionRegistry.Remove(secondHub);
            Assert.Equal(0, EnrollmentSessionRegistry.Count);
        }
        finally
        {
            EnrollmentSessionRegistry.Reset();
        }
    }

    [Fact]
    public void EnrollmentSessionRegistry_CancelRestoresPreviousPendingReservation()
    {
        EnrollmentSessionRegistry.Reset();
        try
        {
            var hub = new ReferenceHub();
            ulong first = EnrollmentSessionRegistry.Reserve("pending@test", false);
            ulong second = EnrollmentSessionRegistry.Reserve("pending@test", false);
            EnrollmentSessionRegistry.Cancel("pending@test", second);

            Assert.True(EnrollmentSessionRegistry.TryActivate(
                "pending@test", first, hub, false, out _));
        }
        finally
        {
            EnrollmentSessionRegistry.Reset();
        }
    }

    [Fact]
    public void EnrollmentSessionRegistry_RollbackIsConditionalAndPreservesOlderSession()
    {
        EnrollmentSessionRegistry.Reset();
        try
        {
            var older = new ReferenceHub();
            var replacement = new ReferenceHub();
            ulong initial = EnrollmentSessionRegistry.Reserve("rollback@test", false);
            Assert.True(EnrollmentSessionRegistry.TryActivate(
                "rollback@test", initial, older, false, out _));
            ulong next = EnrollmentSessionRegistry.Reserve("rollback@test", false);
            Assert.True(EnrollmentSessionRegistry.TryActivate(
                "rollback@test", next, replacement, false, out var previous));
            Assert.Same(older, previous);

            Assert.True(EnrollmentSessionRegistry.TryRollbackActivation(
                "rollback@test", replacement, older));
            EnrollmentSessionRegistry.Remove(replacement);
            Assert.Equal(1, EnrollmentSessionRegistry.Count);
            EnrollmentSessionRegistry.Remove(older);
            Assert.Equal(0, EnrollmentSessionRegistry.Count);
        }
        finally
        {
            EnrollmentSessionRegistry.Reset();
        }
    }

    [Fact]
    public void EnrollmentSessionRegistry_StaleRollbackCannotOverwriteNewerActivation()
    {
        EnrollmentSessionRegistry.Reset();
        try
        {
            var older = new ReferenceHub();
            var replacement = new ReferenceHub();
            var newest = new ReferenceHub();
            ulong initial = EnrollmentSessionRegistry.Reserve("rollback-race@test", false);
            Assert.True(EnrollmentSessionRegistry.TryActivate(
                "rollback-race@test", initial, older, false, out _));
            ulong second = EnrollmentSessionRegistry.Reserve("rollback-race@test", false);
            Assert.True(EnrollmentSessionRegistry.TryActivate(
                "rollback-race@test", second, replacement, false, out _));
            ulong third = EnrollmentSessionRegistry.Reserve("rollback-race@test", false);
            Assert.True(EnrollmentSessionRegistry.TryActivate(
                "rollback-race@test", third, newest, false, out _));

            Assert.False(EnrollmentSessionRegistry.TryRollbackActivation(
                "rollback-race@test", replacement, older));
            EnrollmentSessionRegistry.Remove(replacement);
            Assert.Equal(1, EnrollmentSessionRegistry.Count);
            EnrollmentSessionRegistry.Remove(newest);
            Assert.Equal(0, EnrollmentSessionRegistry.Count);
        }
        finally
        {
            EnrollmentSessionRegistry.Reset();
        }
    }

    [Fact]
    public void RawAndDecodedRateLimiters_AreIndependentAndRemovedWithPeer()
    {
        var hub = new ReferenceHub();
        try
        {
            var raw = AnomalyReplayTable.GetRawRateLimiterOrCreate(hub);
            for (var i = 0; i < 4; i++)
                Assert.True(raw.TryAccept(1, AnomalyChannel.MaxEnvelopeBytes).Allowed);
            Assert.False(raw.TryAccept(1, AnomalyChannel.MaxEnvelopeBytes).Allowed);

            var decoded = AnomalyReplayTable.GetRateLimiterOrCreate(hub);
            Assert.True(decoded.TryAccept(1, 1).Allowed);

            AnomalyReplayTable.Remove(hub);
            Assert.True(
                AnomalyReplayTable.GetRawRateLimiterOrCreate(hub)
                    .TryAccept(1, AnomalyChannel.MaxEnvelopeBytes)
                    .Allowed);
        }
        finally
        {
            AnomalyReplayTable.Reset();
        }
    }

    [Theory]
    [InlineData(0U, 1U)]
    [InlineData(1U, 2U)]
    [InlineData(4294967293U, 4294967294U)]
    public void EncryptedTransportCounterGuard_AdvancesWithoutWrap(uint current, uint expected)
    {
        Assert.True(EncryptedTransportCounterGuard.TryAdvance(current, out var next));
        Assert.Equal(expected, next);
    }

    [Fact]
    public void EncryptedTransportCounterGuard_RejectsExhaustionAndInvalidReceiveCounters()
    {
        Assert.False(EncryptedTransportCounterGuard.TryAdvance(uint.MaxValue - 1, out _));
        Assert.False(EncryptedTransportCounterGuard.TryAdvance(uint.MaxValue, out _));
        Assert.False(EncryptedTransportCounterGuard.CanAccept(10, 0));
        Assert.False(EncryptedTransportCounterGuard.CanAccept(10, 10));
        Assert.False(EncryptedTransportCounterGuard.CanAccept(10, uint.MaxValue));
        Assert.True(EncryptedTransportCounterGuard.CanAccept(10, 11));
    }

    private static ClientHandshake HandshakeFor(
        byte[] identity,
        ClientIdentityRotationChain chain)
    {
        return new ClientHandshake
        {
            identityPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(identity)),
            identityRotationChain = chain,
        };
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "Anomaly-ServerAuthTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Clear(byte[] value)
    {
        if (value != null)
            Array.Clear(value, 0, value.Length);
    }
}
