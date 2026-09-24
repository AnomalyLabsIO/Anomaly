using System;
using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Shared.Auth;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ModAdmissionProtocolTests
{
    [Fact]
    public void InventoryEncodingSortsAndRejectsNoncanonicalOrDuplicateClaims()
    {
        var inventory = new ClientModInventory(new[]
        {
            new ClientModEntry("zeta", "2.0"),
            new ClientModEntry("alpha", "1.0"),
        });
        Assert.Equal(new[] { "alpha", "zeta" }, inventory.Entries.Select(entry => entry.Id));
        Assert.Equal(inventory.Encode(), ClientModInventory.Decode(inventory.Encode()).Encode());

        Assert.Throws<ArgumentException>(() => new ClientModInventory(new[]
        {
            new ClientModEntry("same", "1"), new ClientModEntry("same", "2"),
        }));
        Assert.Throws<ArgumentException>(() => new ClientModInventory(new[]
        {
            new ClientModEntry("Foo", "1"), new ClientModEntry("foo", "2"),
        }));
        Assert.Throws<ArgumentException>(() => new ClientModEntry("bad\nname", "1"));
        Assert.Throws<ArgumentException>(() => new ClientModEntry("bad", "\ud800"));

        byte[] reordered = { 2, 1, (byte)'z', 1, (byte)'1', 1, (byte)'a', 1, (byte)'1' };
        Assert.Throws<InvalidDataException>(() => ClientModInventory.Decode(reordered));
        byte[] malformedUtf8 = { 1, 1, 0xff, 1, (byte)'1' };
        Assert.Throws<InvalidDataException>(() => ClientModInventory.Decode(malformedUtf8));
        Assert.Throws<InvalidDataException>(() => ClientModInventory.Decode(new byte[] { 0, 0 }));
    }

    [Fact]
    public void InventoryBoundsAreEnforcedBeforeLogin()
    {
        Assert.Throws<ArgumentException>(() => new ClientModInventory(
            Enumerable.Range(0, ClientModInventory.MaxEntries + 1)
                .Select(index => new ClientModEntry("mod." + index, "1"))));
        Assert.Throws<ArgumentException>(() => new ClientModEntry(new string('x', ClientModInventory.MaxIdBytes + 1), "1"));
        Assert.Throws<ArgumentException>(() => new ClientModEntry("id", new string('x', ClientModInventory.MaxVersionBytes + 1)));
        Assert.Throws<ArgumentException>(() => new ClientModInventory(
            Enumerable.Range(0, ClientModInventory.MaxEntries)
                .Select(index => new ClientModEntry("mod." + index.ToString("D2") + new string('x', 60), "1.0"))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignedEncryptedLoginCarriesInventoryWithAndWithoutPassword(bool passwordRequired)
    {
        var client = ClientAuthSession.Generate(AuthCrypto.CreateIdentity());
        client.BindConnectionContext(14, 2, 7, false, 0, "example.test:7777");
        client.BindModInventory(new ClientModInventory(new[]
        {
            new ClientModEntry("partner.mod", "1.1"),
            new ClientModEntry(ClientModInventory.CoreId, "1.0.0"),
        }));
        var clientHandshake = client.CreateHandshake();
        var server = ServerAuthSession.Generate(AuthCrypto.CreateIdentity());
        var challenge = passwordRequired
            ? new PasswordChallenge
            {
                Required = true,
                Salt = PasswordAuth.GenerateSalt(),
                Nonce = PasswordAuth.GenerateNonce(),
                Iterations = PasswordAuth.MinimumIterations,
            }
            : PasswordChallenge.NotRequired();
        const string identifier = "mod-admission-test";
        var serverHandshake = server.CreateHandshake(clientHandshake, identifier, challenge);
        var proof = client.CreatePasswordProof("secret", serverHandshake, identifier, challenge);
        var first = client.CreateLoginAttempt(serverHandshake, identifier, challenge, "Test Player", proof);
        var second = client.CreateLoginAttempt(serverHandshake, identifier, challenge, "Test Player", proof);
        Assert.Equal(first.Encode(), second.Encode());
        Assert.InRange(first.Encode().Length, ClientLoginAttempt.MinSize, ClientLoginAttempt.MaxSize);
        Assert.Throws<InvalidOperationException>(() => client.BindModInventory(ClientModInventory.Empty));
        Assert.Throws<InvalidOperationException>(() => client.CreateLoginAttempt(
            serverHandshake, identifier, challenge, "Changed Name", proof));

        Assert.True(server.Validate(clientHandshake, serverHandshake, identifier, challenge,
            "Test Player", ClientLoginAttempt.Decode(first.Encode()), out var decodedProof, out var inventory));
        Assert.Equal(proof, decodedProof);
        Assert.Equal(new[] { ClientModInventory.CoreId, "partner.mod" }, inventory.Entries.Select(entry => entry.Id));
        Assert.Equal("1.1", inventory.Entries[1].Version);
    }

    [Fact]
    public void ChangingInventoryWithoutResigningFailsServerValidation()
    {
        var client = ClientAuthSession.Generate(AuthCrypto.CreateIdentity());
        client.BindConnectionContext(14, 2, 7, false, 0, "example.test:7777");
        client.BindModInventory(new ClientModInventory(new[] { new ClientModEntry("partner.mod", "1.0") }));
        var clientHandshake = client.CreateHandshake();
        var server = ServerAuthSession.Generate(AuthCrypto.CreateIdentity());
        var challenge = PasswordChallenge.NotRequired();
        const string identifier = "inventory-signature-test";
        const string displayName = "Test Player";
        var serverHandshake = server.CreateHandshake(clientHandshake, identifier, challenge);
        var attempt = client.CreateLoginAttempt(serverHandshake, identifier, challenge, displayName, null);

        using (var keys = client.DeriveSessionKeys(serverHandshake, identifier, challenge))
        {
            var aad = AuthTranscript.HashLogin(clientHandshake, serverHandshake, identifier, challenge, displayName);
            var plain = AuthCrypto.AesDecrypt(keys.LoginKey, clientHandshake.nonce, attempt.signature, aad);
            plain[plain.Length - 1] = (byte)'1'; // valid canonical inventory, different version
            attempt.signature = AuthCrypto.AesEncrypt(keys.LoginKey, clientHandshake.nonce, plain, aad);
        }

        Assert.False(server.Validate(clientHandshake, serverHandshake, identifier, challenge,
            displayName, attempt, out var proof, out var inventory));
        Assert.Null(proof);
        Assert.Null(inventory);
        Assert.False(ClientLoginAttempt.TryDecodePlainPayload(new byte[72], out _, out _, out _));
        var legacy = new byte[72];
        Encoding.ASCII.GetBytes("ANLP").CopyTo(legacy, 0);
        legacy[4] = 1;
        Assert.False(ClientLoginAttempt.TryDecodePlainPayload(legacy, out _, out _, out _));
    }

    [Fact]
    public void BoundedJsonRoundTripIncludesTypedIssuesAndOmissionCount()
    {
        var issues = Enumerable.Range(0, 20)
            .Select(index => new ModAdmissionIssue(index % 2 == 0 ? "missing" : "wrong_version",
                "mod." + index.ToString("D2"), "v1.1", index % 2 == 0 ? null : "v1.0"))
            .ToArray();
        var json = ModAdmissionResponseCodec.EncodeBounded(issues);
        Assert.True(Encoding.UTF8.GetByteCount(json) <= ModAdmissionResponseCodec.MaxUtf8Bytes);
        Assert.True(ModAdmissionResponseCodec.TryDecode(json, out var response));
        Assert.Equal(ModAdmissionResponse.Discriminator, response.Kind);
        Assert.Equal(ModAdmissionResponse.SchemaVersion, response.Version);
        Assert.Equal(issues.Length, response.Issues.Count + response.OmittedCount);
        Assert.True(response.OmittedCount > 0);
        Assert.Equal("mod.00", response.Issues[0].Id);
        Assert.Contains("omittedCount", json);
    }

    [Fact]
    public void JsonHandlesUnicodeAndRejectsMalformedOrUnknownResponses()
    {
        var json = ModAdmissionResponseCodec.EncodeBounded(new[]
        {
            new ModAdmissionIssue("blocked", "猫のMod", null, "v1.0"),
            new ModAdmissionIssue("unexpected", "追加", null, "v2.0"),
        });
        Assert.True(Encoding.UTF8.GetByteCount(json) <= ModAdmissionResponseCodec.MaxUtf8Bytes);
        Assert.True(ModAdmissionResponseCodec.TryDecode(json, out var response));
        Assert.Equal("猫のMod", response.Issues[0].Id);
        Assert.False(ModAdmissionResponseCodec.TryDecode("Server is full", out _));
        Assert.False(ModAdmissionResponseCodec.TryDecode("{\"kind\":\"anomaly.mod_admission\",\"version\":2,\"issues\":[],\"omittedCount\":1}", out _));
        Assert.False(ModAdmissionResponseCodec.TryDecode(new string('x', ModAdmissionResponseCodec.MaxUtf8Bytes + 1), out _));
    }

    [Fact]
    public void OversizedSortedIssueDoesNotHideLaterIssuesThatFit()
    {
        var issues = new[]
        {
            new ModAdmissionIssue("wrong_version", "A" + new string('猫', 42),
                new string('\\', 128), new string('\\', 48)),
            new ModAdmissionIssue("missing", "Z.small", "1.0", null),
        };

        var json = ModAdmissionResponseCodec.EncodeBounded(issues);
        Assert.True(Encoding.UTF8.GetByteCount(json) <= ModAdmissionResponseCodec.MaxUtf8Bytes);
        Assert.True(ModAdmissionResponseCodec.TryDecode(json, out var response));
        Assert.Single(response.Issues);
        Assert.Equal("Z.small", response.Issues[0].Id);
        Assert.Equal(1, response.OmittedCount);
    }
}
