using System;
using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ClientIntegrityAuditTests
{
    [Fact]
    public void SixByteWireRecordHasExplicitVersionsFlagsAndOption()
    {
        var audit = new ClientIntegrityAudit(ClientIntegrityAudit.KnownFlags, true);
        Assert.Equal(new byte[] { 1, 1, 0, 31, 0, 1 }, audit.Encode());
        var decoded = ClientIntegrityAudit.Decode(audit.Encode());
        Assert.Equal(audit.Flags, decoded.Flags);
        Assert.True(decoded.IsRisky);
        Assert.True(decoded.AllowExternalAuthenticationModifications);
        Assert.False(new ClientIntegrityAudit(ClientIntegrityAuditFlags.BlockedAttemptObserved, true).IsRisky);
    }

    [Fact]
    public void UnknownVersionsFlagsOptionsAndLengthsAreRejected()
    {
        var clean = new ClientIntegrityAudit(ClientIntegrityAuditFlags.None, false).Encode();
        foreach (int offset in new[] { 0, 1, 2, 3, 4, 5 })
        {
            var invalid = (byte[])clean.Clone();
            invalid[offset] = 255;
            Assert.Throws<InvalidDataException>(() => ClientIntegrityAudit.Decode(invalid));
        }
        Assert.Throws<InvalidDataException>(() => ClientIntegrityAudit.Decode(clean.Take(5).ToArray()));
        Assert.Throws<InvalidDataException>(() => ClientIntegrityAudit.Decode(clean.Concat(new byte[1]).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientIntegrityAudit((ClientIntegrityAuditFlags)32, false));
    }

    [Fact]
    public void LoginRequiresAtomicAdmissionBindingAndRejectsRebinding()
    {
        var pair = CreatePair();
        Assert.Throws<InvalidOperationException>(() => CreateLogin(pair));
        Assert.Throws<ArgumentNullException>(() => pair.Client.BindAdmission(ClientModInventory.Empty, null));
        pair.Client.BindAdmission(ClientModInventory.Empty, new ClientIntegrityAudit(ClientIntegrityAuditFlags.None, false));
        var first = CreateLogin(pair);
        Assert.Equal(first.Encode(), CreateLogin(pair).Encode());
        Assert.Throws<InvalidOperationException>(() => pair.Client.BindAdmission(ClientModInventory.Empty,
            new ClientIntegrityAudit(ClientIntegrityAuditFlags.CoverageIncomplete, true)));
        Assert.Equal(first.Encode(), CreateLogin(pair).Encode());
        var fresh = CreatePair();
        Assert.NotEqual(pair.Handshake.nonce, fresh.Handshake.nonce);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifiedLoginReturnsAuditWithoutTreatingOptInAsRisk(bool optIn)
    {
        var pair = CreatePair();
        var expected = new ClientIntegrityAudit(ClientIntegrityAuditFlags.BlockedAttemptObserved, optIn);
        pair.Client.BindAdmission(ClientModInventory.Empty, expected);
        Assert.True(pair.Server.Validate(pair.Handshake, pair.Response, "integrity-test",
            PasswordChallenge.NotRequired(), "Player", CreateLogin(pair), out _, out var inventory, out var audit));
        Assert.NotNull(inventory);
        Assert.Equal(expected.Encode(), audit.Encode());
        Assert.False(audit.IsRisky);
    }

    [Theory]
    [InlineData(3)] // change only a valid risk bit
    [InlineData(5)] // change only the opt-in option
    public void AuditCannotBeChangedWithoutResigningEvenWithTheEncryptionKey(int auditOffset)
    {
        var pair = CreatePair();
        pair.Client.BindAdmission(ClientModInventory.Empty, new ClientIntegrityAudit(ClientIntegrityAuditFlags.None, false));
        var attempt = CreateLogin(pair);
        using (var keys = pair.Client.DeriveSessionKeys(pair.Response, "integrity-test", PasswordChallenge.NotRequired()))
        {
            var aad = AuthTranscript.HashLogin(pair.Handshake, pair.Response, "integrity-test", PasswordChallenge.NotRequired(), "Player");
            var plain = AuthCrypto.AesDecrypt(keys.LoginKey, pair.Handshake.nonce, attempt.signature, aad);
            plain[plain.Length - ClientIntegrityAudit.EncodedSize + auditOffset] = 1;
            attempt.signature = AuthCrypto.AesEncrypt(keys.LoginKey, pair.Handshake.nonce, plain, aad);
        }
        Assert.False(pair.Server.Validate(pair.Handshake, pair.Response, "integrity-test",
            PasswordChallenge.NotRequired(), "Player", attempt, out var proof, out var inventory, out var audit));
        Assert.Null(proof);
        Assert.Null(inventory);
        Assert.Null(audit);
    }

    [Fact]
    public void MissingAuditOldPayloadAndTrailingBytesExposeNoPartialClaims()
    {
        var plain = ClientLoginAttempt.EncodePlainPayload(new byte[72], null,
            ClientModInventory.Empty.Encode(), new ClientIntegrityAudit(ClientIntegrityAuditFlags.None, false).Encode());
        var oldPayload = (byte[])plain.Clone();
        oldPayload[4] = 2;
        foreach (var bad in new[] { plain.Take(plain.Length - 6).ToArray(), oldPayload, plain.Concat(new byte[1]).ToArray() })
        {
            Assert.False(ClientLoginAttempt.TryDecodePlainPayload(bad, out var signature, out var proof, out var inventory, out var audit));
            Assert.Null(signature);
            Assert.Null(proof);
            Assert.Null(inventory);
            Assert.Null(audit);
        }
        var handshake = CreatePair().Handshake.Encode();
        handshake[0] = 7;
        Assert.Throws<AnomalyProtocolVersionMismatchException>(() => ClientHandshake.Decode(handshake));
    }

    [Fact]
    public void MaximumInventoryAuditAndPasswordRemainWithinLoginBound()
    {
        var entries = Enumerable.Range(0, 32).Select(index =>
            new ClientModEntry("m" + index.ToString("D2") + (index == 0 ? new string('x', 31) : ""), new string('1', 18)));
        var inventory = new ClientModInventory(entries).Encode();
        Assert.Equal(ClientModInventory.MaxEncodedBytes, inventory.Length);
        var payload = ClientLoginAttempt.EncodePlainPayload(new byte[72], new byte[PasswordAuth.ProofSize], inventory,
            new ClientIntegrityAudit(ClientIntegrityAudit.KnownFlags, true).Encode());
        Assert.True(payload.Length + 16 <= ClientLoginAttempt.MaxSize);
        var largest = ClientLoginAttempt.EncodePlainPayload(new byte[128], new byte[PasswordAuth.ProofSize], inventory,
            new ClientIntegrityAudit(ClientIntegrityAudit.KnownFlags, true).Encode());
        Assert.Equal(960, largest.Length + 16);
        Assert.Throws<ArgumentException>(() => ClientLoginAttempt.EncodePlainPayload(new byte[129],
            new byte[PasswordAuth.ProofSize], inventory, new ClientIntegrityAudit(ClientIntegrityAudit.KnownFlags, true).Encode()));
    }

    [Fact]
    public void IntegrityRejectionIsBoundedTypedAndRejectsMalformedOrNonRiskyClaims()
    {
        var json = ClientIntegrityResponseCodec.Encode(ClientIntegrityAudit.KnownFlags);
        Assert.True(Encoding.UTF8.GetByteCount(json) <= ClientIntegrityResponseCodec.MaxUtf8Bytes);
        Assert.True(ClientIntegrityResponseCodec.TryDecode(json, out var response));
        Assert.Equal(ClientIntegrityAudit.KnownFlags, response.Flags);
        Assert.Contains("anomaly.authentication_integrity", json);
        Assert.Throws<ArgumentOutOfRangeException>(() => ClientIntegrityResponseCodec.Encode(ClientIntegrityAuditFlags.BlockedAttemptObserved));
        Assert.False(ClientIntegrityResponseCodec.TryDecode(json.Replace("\"flags\":31", "\"flags\":32"), out _));
        Assert.False(ClientIntegrityResponseCodec.TryDecode(json.Replace("\"version\":1", "\"version\":2"), out _));
        Assert.False(ClientIntegrityResponseCodec.TryDecode(json + "{}", out _));
        Assert.False(ClientIntegrityResponseCodec.TryDecode(new string('x', 461), out _));
    }

    private static ClientLoginAttempt CreateLogin(Pair pair) => pair.Client.CreateLoginAttempt(
        pair.Response, "integrity-test", PasswordChallenge.NotRequired(), "Player", null);

    private static Pair CreatePair()
    {
        var client = ClientAuthSession.Generate(AuthCrypto.CreateIdentity());
        client.BindConnectionContext(14, 2, 7, false, 0, "example.test:7777");
        var handshake = client.CreateHandshake();
        var server = ServerAuthSession.Generate(AuthCrypto.CreateIdentity());
        return new Pair
        {
            Client = client, Server = server, Handshake = handshake,
            Response = server.CreateHandshake(handshake, "integrity-test", PasswordChallenge.NotRequired()),
        };
    }

    private sealed class Pair
    {
        internal ClientAuthSession Client;
        internal ServerAuthSession Server;
        internal ClientHandshake Handshake;
        internal ServerHandshake Response;
    }
}
