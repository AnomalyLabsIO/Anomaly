using System;
using System.Net;
using System.Linq;
using Anomaly.Server.Auth;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class AuthProtocolV2Tests
{
    [Fact]
    public void SignedTranscriptAndKeyScheduleAgreeAcrossPeers()
    {
        var pair = CreatePair();

        Assert.True(pair.Client.ValidateServerHandshake(
            pair.ServerHandshake,
            pair.Identifier,
            pair.PasswordChallenge));

        var attempt = pair.Client.CreateLoginAttempt(
            pair.ServerHandshake,
            pair.Identifier,
            pair.PasswordChallenge,
            "Test Player",
            null);
        Assert.True(pair.Server.Validate(
            pair.ClientHandshake,
            pair.ServerHandshake,
            pair.Identifier,
            pair.PasswordChallenge,
            "Test Player",
            attempt,
            out var passwordProof));
        Assert.Null(passwordProof);

        using (var clientKeys = pair.Client.DeriveSessionKeys(
                   pair.ServerHandshake,
                   pair.Identifier,
                   pair.PasswordChallenge))
        using (var serverKeys = pair.Server.DeriveSessionKeys(
                   pair.ClientHandshake,
                   pair.ServerHandshake,
                   pair.Identifier,
                   pair.PasswordChallenge))
        {
            Assert.Equal(clientKeys.ClientToServerKey, serverKeys.ClientToServerKey);
            Assert.Equal(clientKeys.ServerToClientKey, serverKeys.ServerToClientKey);
            Assert.Null(clientKeys.LegacyTransportKey);
            Assert.Null(serverKeys.LegacyTransportKey);
            Assert.Equal(clientKeys.SessionId, serverKeys.SessionId);
            Assert.False(clientKeys.ClientToServerKey.SequenceEqual(clientKeys.ServerToClientKey));
            Assert.Equal(16, clientKeys.SessionId.Length);
        }
    }

    [Fact]
    public void WireRoundTripPreservesRetryBindingAndSignedServerHandshake()
    {
        var client = ClientAuthSession.Generate(AuthCrypto.CreateIdentity());
        BindDefaultContext(client);
        var cookie = Enumerable.Range(0, ClientHandshake.RetryCookieSize).Select(i => (byte)i).ToArray();
        var decodedClientHandshake = ClientHandshake.Decode(client.CreateHandshake(cookie).Encode());
        Assert.Equal(cookie, decodedClientHandshake.retryCookie);
        Assert.Equal("example.test:7777", decodedClientHandshake.connectionContext);
        Assert.True(decodedClientHandshake.MatchesGameVersion(14, 2, 7, false, 0));

        var server = ServerAuthSession.Generate(AuthCrypto.CreateIdentity());
        var identifier = "wire-round-trip";
        var challenge = PasswordChallenge.NotRequired();
        var decodedServerHandshake = ServerHandshake.Decode(
            server.CreateHandshake(decodedClientHandshake, identifier, challenge).Encode());

        Assert.True(client.ValidateServerHandshake(decodedServerHandshake, identifier, challenge));
        var attempt = client.CreateLoginAttempt(
            decodedServerHandshake,
            identifier,
            challenge,
            "Wire Player",
            null);
        Assert.True(server.Validate(
            decodedClientHandshake,
            decodedServerHandshake,
            identifier,
            challenge,
            "Wire Player",
            ClientLoginAttempt.Decode(attempt.Encode()),
            out _));
    }

    [Fact]
    public void ChangingServerIdentifierInvalidatesLoginAttempt()
    {
        var pair = CreatePair();
        var attempt = pair.Client.CreateLoginAttempt(
            pair.ServerHandshake,
            pair.Identifier,
            pair.PasswordChallenge,
            "Test Player",
            null);

        Assert.False(pair.Server.Validate(
            pair.ClientHandshake,
            pair.ServerHandshake,
            pair.Identifier + "-substituted",
            pair.PasswordChallenge,
            "Test Player",
            attempt,
            out _));
    }

    [Fact]
    public void ChangingDisplayNameInvalidatesLoginAttempt()
    {
        var pair = CreatePair();
        var attempt = pair.Client.CreateLoginAttempt(
            pair.ServerHandshake,
            pair.Identifier,
            pair.PasswordChallenge,
            "Original Name",
            null);

        Assert.False(pair.Server.Validate(
            pair.ClientHandshake,
            pair.ServerHandshake,
            pair.Identifier,
            pair.PasswordChallenge,
            "Substituted Name",
            attempt,
            out _));
    }

    [Fact]
    public void ChangingServerEphemeralKeyInvalidatesServerSignature()
    {
        var pair = CreatePair();
        pair.ServerHandshake.sessionPublic[0] ^= 0x40;

        Assert.False(pair.Client.ValidateServerHandshake(
            pair.ServerHandshake,
            pair.Identifier,
            pair.PasswordChallenge));
    }

    [Fact]
    public void ChangingPasswordPolicyInvalidatesServerSignature()
    {
        var pair = CreatePair();
        var injectedPolicy = new PasswordChallenge
        {
            Required = true,
            Salt = Enumerable.Repeat((byte)1, PasswordAuth.SaltSize).ToArray(),
            Iterations = PasswordAuth.MinimumIterations,
            Nonce = Enumerable.Repeat((byte)2, PasswordAuth.NonceSize).ToArray(),
        };

        Assert.False(pair.Client.ValidateServerHandshake(
            pair.ServerHandshake,
            pair.Identifier,
            injectedPolicy));
    }

    [Fact]
    public void ChangingSignedGameVersionInvalidatesServerHandshake()
    {
        var client = ClientAuthSession.Generate(AuthCrypto.CreateIdentity());
        BindDefaultContext(client);
        var tampered = ClientHandshake.Decode(client.CreateHandshake().Encode());
        tampered.gameRevision++;

        var challenge = PasswordChallenge.NotRequired();
        var server = ServerAuthSession.Generate(AuthCrypto.CreateIdentity());
        var response = server.CreateHandshake(tampered, "game-version-mutation", challenge);

        Assert.False(client.ValidateServerHandshake(response, "game-version-mutation", challenge));
    }

    [Fact]
    public void ChangingNormalizedConnectionContextInvalidatesServerHandshake()
    {
        var client = ClientAuthSession.Generate(AuthCrypto.CreateIdentity());
        BindDefaultContext(client);
        var tampered = ClientHandshake.Decode(client.CreateHandshake().Encode());
        tampered.connectionContext = "relay.example:7777";

        var challenge = PasswordChallenge.NotRequired();
        var server = ServerAuthSession.Generate(AuthCrypto.CreateIdentity());
        var response = server.CreateHandshake(tampered, "endpoint-mutation", challenge);

        Assert.False(client.ValidateServerHandshake(response, "endpoint-mutation", challenge));
    }

    [Fact]
    public void SignedServerIdentityRotationRoundTripsAndAuthorizesNewKey()
    {
        const long now = 1_700_000_000;
        byte[] oldIdentity = AuthCrypto.CreateIdentity();
        byte[] newIdentity = AuthCrypto.CreateIdentity();
        byte[] oldPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(oldIdentity));
        byte[] newPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(newIdentity));
        var record = ServerIdentityRotationRecord.Create(
            oldIdentity,
            newIdentity,
            now - 60,
            now + 3600,
            4);

        var decoded = ServerIdentityRotationRecord.Decode(record.Encode());
        Assert.True(decoded.Validate(oldPublic, newPublic, now, 3, out var error), error);

        var client = ClientAuthSession.Generate(AuthCrypto.CreateIdentity());
        BindDefaultContext(client);
        var clientHandshake = client.CreateHandshake();
        var server = ServerAuthSession.Generate(newIdentity);
        var challenge = PasswordChallenge.NotRequired();
        var handshake = ServerHandshake.Decode(
            server.CreateHandshake(clientHandshake, "rotated-server", challenge, decoded).Encode());

        Assert.True(client.ValidateServerHandshake(handshake, "rotated-server", challenge));
        Assert.NotNull(handshake.identityRotationRecord);
        Assert.Equal((ulong)4, handshake.identityRotationRecord.Sequence);
    }

    [Fact]
    public void ServerIdentityRotationRejectsMutationAndWrongOldKey()
    {
        const long now = 1_700_000_000;
        byte[] oldIdentity = AuthCrypto.CreateIdentity();
        byte[] newIdentity = AuthCrypto.CreateIdentity();
        byte[] wrongOldIdentity = AuthCrypto.CreateIdentity();
        byte[] oldPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(oldIdentity));
        byte[] wrongOldPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(wrongOldIdentity));
        byte[] newPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(newIdentity));
        var record = ServerIdentityRotationRecord.Create(
            oldIdentity,
            newIdentity,
            now - 60,
            now + 3600,
            9);

        Assert.False(record.Validate(wrongOldPublic, newPublic, now, 8, out _));

        var signatureMutation = record.Clone();
        signatureMutation.Signature[0] ^= 0x80;
        Assert.False(signatureMutation.Validate(oldPublic, newPublic, now, 8, out _));

        var payloadMutation = record.Clone();
        payloadMutation.NewFingerprint[0] ^= 0x40;
        Assert.False(payloadMutation.Validate(oldPublic, newPublic, now, 8, out _));
    }

    [Fact]
    public void ServerIdentityRotationRejectsReplayExpiryAndUnboundedValidity()
    {
        const long now = 1_700_000_000;
        byte[] oldIdentity = AuthCrypto.CreateIdentity();
        byte[] newIdentity = AuthCrypto.CreateIdentity();
        byte[] oldPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(oldIdentity));
        byte[] newPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(newIdentity));
        var record = ServerIdentityRotationRecord.Create(
            oldIdentity,
            newIdentity,
            now - 60,
            now + 60,
            12);

        Assert.False(record.Validate(oldPublic, newPublic, now, 12, out var replayError));
        Assert.Contains("replayed", replayError);
        Assert.False(record.Validate(oldPublic, newPublic, now + 61, 11, out var expiryError));
        Assert.Contains("expired", expiryError);

        Assert.ThrowsAny<Exception>(() => ServerIdentityRotationRecord.Create(
            oldIdentity,
            newIdentity,
            now,
            now + ServerIdentityRotationRecord.MaximumValiditySeconds + 1,
            13));
    }

    [Fact]
    public void RemovingRotationRecordInvalidatesSignedServerTranscript()
    {
        const long now = 1_700_000_000;
        byte[] oldIdentity = AuthCrypto.CreateIdentity();
        byte[] newIdentity = AuthCrypto.CreateIdentity();
        var record = ServerIdentityRotationRecord.Create(
            oldIdentity,
            newIdentity,
            now - 60,
            now + 3600,
            1);
        var client = ClientAuthSession.Generate(AuthCrypto.CreateIdentity());
        BindDefaultContext(client);
        var clientHandshake = client.CreateHandshake();
        var server = ServerAuthSession.Generate(newIdentity);
        var challenge = PasswordChallenge.NotRequired();
        var handshake = server.CreateHandshake(clientHandshake, "rotation-strip", challenge, record);

        handshake.identityRotationRecord = null;

        Assert.False(client.ValidateServerHandshake(handshake, "rotation-strip", challenge));
    }

    [Fact]
    public void ClientIdentityRotationPreservesEnrollmentWhileCurrentKeyAuthenticates()
    {
        byte[] originalIdentity = AuthCrypto.CreateIdentity();
        byte[] secondIdentity = AuthCrypto.CreateIdentity();
        byte[] currentIdentity = AuthCrypto.CreateIdentity();
        var chain = ClientIdentityRotationChain
            .Create(originalIdentity, secondIdentity)
            .Append(secondIdentity, currentIdentity);

        var client = ClientAuthSession.Generate(currentIdentity);
        BindDefaultContext(client);
        client.BindIdentityRotationChain(chain);
        var clientHandshake = ClientHandshake.Decode(client.CreateHandshake().Encode());
        byte[] originalPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(originalIdentity));
        byte[] currentPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(currentIdentity));

        Assert.Equal(originalPublic, clientHandshake.GetEnrollmentIdentityPublic());
        Assert.Equal(currentPublic, clientHandshake.identityPublic);

        var server = ServerAuthSession.Generate(AuthCrypto.CreateIdentity());
        var challenge = PasswordChallenge.NotRequired();
        var serverHandshake = server.CreateHandshake(clientHandshake, "client-rotation", challenge);
        Assert.True(client.ValidateServerHandshake(serverHandshake, "client-rotation", challenge));
        var attempt = client.CreateLoginAttempt(
            serverHandshake,
            "client-rotation",
            challenge,
            "Rotated Player",
            null);
        Assert.True(server.Validate(
            clientHandshake,
            serverHandshake,
            "client-rotation",
            challenge,
            "Rotated Player",
            attempt,
            out _));

        // Losing the old key/chain intentionally creates a fresh enrollment even
        // though the current private key remains usable.
        var freshSession = ClientAuthSession.Generate(currentIdentity);
        BindDefaultContext(freshSession);
        var freshHandshake = freshSession.CreateHandshake();
        Assert.Equal(currentPublic, freshHandshake.GetEnrollmentIdentityPublic());
        Assert.False(originalPublic.SequenceEqual(freshHandshake.GetEnrollmentIdentityPublic()));
    }

    [Fact]
    public void ClientIdentityRotationRejectsMutationWrongSuccessorAndSequenceReplay()
    {
        byte[] originalIdentity = AuthCrypto.CreateIdentity();
        byte[] secondIdentity = AuthCrypto.CreateIdentity();
        byte[] currentIdentity = AuthCrypto.CreateIdentity();
        var chain = ClientIdentityRotationChain
            .Create(originalIdentity, secondIdentity)
            .Append(secondIdentity, currentIdentity);
        byte[] currentPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(currentIdentity));

        var signatureMutation = chain.Clone();
        signatureMutation.Records[0].Signature[0] ^= 0x20;
        Assert.False(signatureMutation.TryValidate(currentPublic, out _, out _));

        var sequenceReplay = chain.Clone();
        sequenceReplay.Records[1].Sequence = 1;
        Assert.False(sequenceReplay.TryValidate(currentPublic, out _, out var sequenceError));
        Assert.Contains("sequence", sequenceError);

        byte[] unrelatedPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(AuthCrypto.CreateIdentity()));
        Assert.False(chain.TryValidate(unrelatedPublic, out _, out var successorError));
        Assert.Contains("terminate", successorError);
    }

    [Fact]
    public void RemovingClientRotationChainInvalidatesServerAndLoginTranscripts()
    {
        byte[] originalIdentity = AuthCrypto.CreateIdentity();
        byte[] currentIdentity = AuthCrypto.CreateIdentity();
        var chain = ClientIdentityRotationChain.Create(originalIdentity, currentIdentity);
        var client = ClientAuthSession.Generate(currentIdentity);
        BindDefaultContext(client);
        client.BindIdentityRotationChain(chain);
        var clientHandshake = client.CreateHandshake();
        var server = ServerAuthSession.Generate(AuthCrypto.CreateIdentity());
        var challenge = PasswordChallenge.NotRequired();
        var serverHandshake = server.CreateHandshake(clientHandshake, "client-chain-strip", challenge);
        var attempt = client.CreateLoginAttempt(
            serverHandshake,
            "client-chain-strip",
            challenge,
            "Chain Player",
            null);

        var stripped = ClientHandshake.Decode(clientHandshake.Encode());
        stripped.identityRotationChain = null;

        Assert.False(server.Validate(
            stripped,
            serverHandshake,
            "client-chain-strip",
            challenge,
            "Chain Player",
            attempt,
            out _));
    }

    [Fact]
    public void ClientIdentityRotationChainHasDeterministicDepthCeiling()
    {
        byte[] first = AuthCrypto.CreateIdentity();
        byte[] current = AuthCrypto.CreateIdentity();
        var chain = ClientIdentityRotationChain.Create(first, current);
        for (var i = 1; i < ClientIdentityRotationChain.MaximumLinks; i++)
        {
            byte[] next = AuthCrypto.CreateIdentity();
            chain = chain.Append(current, next);
            current = next;
        }

        byte[] rejected = AuthCrypto.CreateIdentity();
        var error = Assert.Throws<InvalidOperationException>(() => chain.Append(current, rejected));
        Assert.Contains("ceiling", error.Message);
    }

    [Fact]
    public void ClientRotationSignaturesAreNeverVerifiedBeforeRetryCookie()
    {
        const long now = 1_700_000_000;
        byte[] originalIdentity = AuthCrypto.CreateIdentity();
        byte[] secondIdentity = AuthCrypto.CreateIdentity();
        byte[] currentIdentity = AuthCrypto.CreateIdentity();
        var chain = ClientIdentityRotationChain
            .Create(originalIdentity, secondIdentity)
            .Append(secondIdentity, currentIdentity);
        var client = ClientAuthSession.Generate(currentIdentity);
        BindDefaultContext(client);
        client.BindIdentityRotationChain(chain);
        byte[] cookieLessWire = client.CreateHandshake().Encode();
        var address = IPAddress.Parse("192.0.2.50");

        PreauthGate.InitializeForTests(() => now);
        ClientIdentityRotationRecord.ResetSignatureVerificationCount();
        try
        {
            for (var i = 0; i < 10_000; i++)
            {
                var parsed = ClientHandshake.Decode(cookieLessWire);
                Assert.False(PreauthGate.ValidateRetryCookie(address, parsed));
            }
            Assert.Equal(0, ClientIdentityRotationRecord.SignatureVerificationCount);

            var cookieLess = ClientHandshake.Decode(cookieLessWire);
            byte[] cookie = PreauthGate.CreateRetryCookie(address, cookieLess);
            Assert.Equal(0, ClientIdentityRotationRecord.SignatureVerificationCount);

            cookieLess.retryCookie = cookie;
            var cookieHandshake = ClientHandshake.Decode(cookieLess.Encode());
            Assert.True(PreauthGate.ValidateRetryCookie(address, cookieHandshake));
            Assert.Equal(0, ClientIdentityRotationRecord.SignatureVerificationCount);

            Assert.True(cookieHandshake.TryValidateIdentityRotationChain(out _, out var error), error);
            Assert.Equal(chain.Records.Count, ClientIdentityRotationRecord.SignatureVerificationCount);
        }
        finally
        {
            PreauthGate.Reset();
            ClientIdentityRotationRecord.ResetSignatureVerificationCount();
        }
    }

    [Theory]
    [InlineData("EXAMPLE.test.", 7777, "example.test:7777")]
    [InlineData("[2001:0db8::1]", 7777, "[2001:db8::1]:7777")]
    [InlineData("::ffff:192.0.2.10", 7777, "192.0.2.10:7777")]
    public void ConnectionContextUsesOneCanonicalEndpointForm(
        string host,
        int port,
        string expected)
    {
        string normalized = AuthConnectionContext.NormalizeEndpoint(host, port);
        Assert.Equal(expected, normalized);
        Assert.True(AuthConnectionContext.IsNormalizedEndpoint(normalized));
    }

    [Fact]
    public void ProtocolV1HandshakeIsRejectedWithoutDowngrade()
    {
        var encoded = new byte[ClientHandshake.MinimumSize];
        encoded[0] = 1;

        var error = Assert.Throws<AnomalyProtocolVersionMismatchException>(
            () => ClientHandshake.Decode(encoded));
        Assert.Equal((byte)1, error.PeerVersion);
        Assert.Equal(AnomalyProtocol.CurrentVersion, error.OurVersion);
    }

    [Fact]
    public void PasswordIterationsHaveStrictUpperBound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordAuth.DeriveVerifier(
            "password",
            new byte[PasswordAuth.SaltSize],
            PasswordAuth.MaximumIterations + 1));
    }

    [Fact]
    public void AllZeroX25519PublicKeyIsRejected()
    {
        var exception = Record.Exception(() => AuthCrypto.ComputeSharedSecret(
            AuthCrypto.CreateX25519KeyPair(),
            new byte[32]));
        Assert.NotNull(exception);
    }

    [Fact]
    public void DirectionalTransportRejectsReflectionAndWrongSession()
    {
        var pair = CreatePair();
        using (var clientKeys = pair.Client.DeriveSessionKeys(
                   pair.ServerHandshake,
                   pair.Identifier,
                   pair.PasswordChallenge))
        using (var serverKeys = pair.Server.DeriveSessionKeys(
                   pair.ClientHandshake,
                   pair.ServerHandshake,
                   pair.Identifier,
                   pair.PasswordChallenge))
        {
            var plaintext = new byte[] { AnomalyChannel.ChannelId, 1, 0, 0, 0, 2, 0, 0, 0 };
            var envelope = AuthTransportCrypto.Encrypt(
                clientKeys.ClientToServerKey,
                clientKeys.SessionId,
                clientKeys.TranscriptHash,
                AuthTransportDirection.ClientToServer,
                plaintext);

            Assert.Equal(plaintext, AuthTransportCrypto.Decrypt(
                serverKeys.ClientToServerKey,
                serverKeys.SessionId,
                serverKeys.TranscriptHash,
                AuthTransportDirection.ClientToServer,
                envelope));

            // Even with the same key, the opposite-direction AAD must reject a
            // reflected ciphertext before payload dispatch.
            Assert.ThrowsAny<Exception>(() => AuthTransportCrypto.Decrypt(
                serverKeys.ClientToServerKey,
                serverKeys.SessionId,
                serverKeys.TranscriptHash,
                AuthTransportDirection.ServerToClient,
                envelope));

            var wrongSession = (byte[])serverKeys.SessionId.Clone();
            wrongSession[0] ^= 0x80;
            Assert.ThrowsAny<Exception>(() => AuthTransportCrypto.Decrypt(
                serverKeys.ClientToServerKey,
                wrongSession,
                serverKeys.TranscriptHash,
                AuthTransportDirection.ClientToServer,
                envelope));
        }
    }

    [Fact]
    public void ReplayCounterIsRejectedAfterDirectionalDecryption()
    {
        var pair = CreatePair();
        using (var clientKeys = pair.Client.DeriveSessionKeys(
                   pair.ServerHandshake,
                   pair.Identifier,
                   pair.PasswordChallenge))
        using (var serverKeys = pair.Server.DeriveSessionKeys(
                   pair.ClientHandshake,
                   pair.ServerHandshake,
                   pair.Identifier,
                   pair.PasswordChallenge))
        {
            const uint counter = 42;
            var plaintext = new byte[9];
            plaintext[0] = AnomalyChannel.ChannelId;
            BitConverter.GetBytes(counter).CopyTo(plaintext, 1);
            var envelope = AuthTransportCrypto.Encrypt(
                clientKeys.ClientToServerKey,
                clientKeys.SessionId,
                clientKeys.TranscriptHash,
                AuthTransportDirection.ClientToServer,
                plaintext);
            var decrypted = AuthTransportCrypto.Decrypt(
                serverKeys.ClientToServerKey,
                serverKeys.SessionId,
                serverKeys.TranscriptHash,
                AuthTransportDirection.ClientToServer,
                envelope);

            var replay = new AnomalyReplayState();
            Assert.Equal(
                AnomalyReplayState.RxResult.Accepted,
                replay.TryAcceptRx(MessageChannel.ReliableOrdered, BitConverter.ToUInt32(decrypted, 1), out _));
            Assert.Equal(
                AnomalyReplayState.RxResult.RejectedReplay,
                replay.TryAcceptRx(MessageChannel.ReliableOrdered, BitConverter.ToUInt32(decrypted, 1), out _));
        }
    }

    [Fact]
    public void PreauthLimiterBoundsIpv4SubnetBeforeGlobalCapacity()
    {
        long now = 1_700_000_000;
        PreauthGate.InitializeForTests(() => now);
        try
        {
            for (var host = 1; host <= 48; host++)
                Assert.True(PreauthGate.TryAcceptRequest(IPAddress.Parse("10.20.30." + host)));

            Assert.False(PreauthGate.TryAcceptRequest(IPAddress.Parse("10.20.30.200")));
            Assert.True(PreauthGate.TryAcceptRequest(IPAddress.Parse("10.20.31.1")));

            now++;
            Assert.True(PreauthGate.TryAcceptRequest(IPAddress.Parse("10.20.30.201")));
        }
        finally
        {
            PreauthGate.Reset();
        }
    }

    [Fact]
    public void PreauthSubnetKeysUseIpv4Slash24AndIpv6Slash64()
    {
        Assert.Equal(
            PreauthGate.NormalizeSubnet(IPAddress.Parse("192.0.2.1")),
            PreauthGate.NormalizeSubnet(IPAddress.Parse("192.0.2.254")));
        Assert.NotEqual(
            PreauthGate.NormalizeSubnet(IPAddress.Parse("192.0.2.1")),
            PreauthGate.NormalizeSubnet(IPAddress.Parse("192.0.3.1")));
        Assert.Equal(
            PreauthGate.NormalizeSubnet(IPAddress.Parse("2001:db8:1:2::1")),
            PreauthGate.NormalizeSubnet(IPAddress.Parse("2001:db8:1:2:ffff::1")));
    }

    [Fact]
    public void LegacyBidirectionalKeyRequiresExplicitLoopbackPeer()
    {
        var pair = CreatePair();
        Assert.Throws<InvalidOperationException>(() => pair.Client.DeriveLoopbackMigrationSessionKeys(
            pair.ServerHandshake,
            pair.Identifier,
            pair.PasswordChallenge,
            IPAddress.Parse("203.0.113.10")));

        using (var clientKeys = pair.Client.DeriveLoopbackMigrationSessionKeys(
                   pair.ServerHandshake,
                   pair.Identifier,
                   pair.PasswordChallenge,
                   IPAddress.Loopback))
        using (var serverKeys = pair.Server.DeriveLoopbackMigrationSessionKeys(
                   pair.ClientHandshake,
                   pair.ServerHandshake,
                   pair.Identifier,
                   pair.PasswordChallenge,
                   IPAddress.Loopback))
        {
            Assert.NotNull(clientKeys.LegacyTransportKey);
            Assert.Equal(clientKeys.LegacyTransportKey, serverKeys.LegacyTransportKey);
        }
    }

    private static Pair CreatePair()
    {
        var client = ClientAuthSession.Generate(AuthCrypto.CreateIdentity());
        BindDefaultContext(client);
        var clientHandshake = client.CreateHandshake();
        var server = ServerAuthSession.Generate(AuthCrypto.CreateIdentity());
        var passwordChallenge = PasswordChallenge.NotRequired();
        var identifier = "test-session-identifier";
        var serverHandshake = server.CreateHandshake(clientHandshake, identifier, passwordChallenge);
        return new Pair
        {
            Client = client,
            Server = server,
            ClientHandshake = clientHandshake,
            ServerHandshake = serverHandshake,
            PasswordChallenge = passwordChallenge,
            Identifier = identifier,
        };
    }

    private static void BindDefaultContext(ClientAuthSession client)
    {
        client.BindConnectionContext(
            14,
            2,
            7,
            false,
            0,
            AuthConnectionContext.NormalizeEndpoint("example.test", 7777));
    }

    private sealed class Pair
    {
        public ClientAuthSession Client;
        public ServerAuthSession Server;
        public ClientHandshake ClientHandshake;
        public ServerHandshake ServerHandshake;
        public PasswordChallenge PasswordChallenge;
        public string Identifier;
    }
}
