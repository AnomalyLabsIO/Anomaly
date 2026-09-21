using System;
using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Client.Auth;
using Anomaly.Shared.Auth;
using Newtonsoft.Json;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class PlayerAuthProfileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Anomaly.PlayerAuthProfileStore.Tests",
        Guid.NewGuid().ToString("N"));

    private string ProfilePath => Path.Combine(_directory, "user.json");

    [Fact]
    public void FreshInstanceProfilesRemainIndependentAndSurviveReopening()
    {
        byte[] firstIdentity = AuthCrypto.CreateIdentity();
        byte[] secondIdentity = AuthCrypto.CreateIdentity();
        try
        {
            string firstPath = Path.Combine(_directory, "first", "UserData", "Anomaly", "user.json");
            string secondPath = Path.Combine(_directory, "second", "UserData", "Anomaly", "user.json");
            var firstStore = new PlayerAuthProfileStore(firstPath, Transform, Transform, ReplaceFile);
            var secondStore = new PlayerAuthProfileStore(secondPath, Transform, Transform, ReplaceFile);
            Assert.False(firstStore.Exists);
            Assert.False(secondStore.Exists);

            var firstProfile = new PlayerAuth { Username = "Alice" };
            firstProfile.SetIdentity(firstIdentity, Transform);
            firstStore.Save(firstProfile);
            byte[] firstBytes = File.ReadAllBytes(firstPath);
            Assert.False(secondStore.Exists);

            var secondProfile = new PlayerAuth { Username = "Bob" };
            secondProfile.SetIdentity(secondIdentity, Transform);
            secondStore.Save(secondProfile);
            Assert.NotEqual(firstProfile.IdentityPublic, secondProfile.IdentityPublic);

            var reopened = new PlayerAuthProfileStore(firstPath, Transform, Transform, ReplaceFile);
            Assert.True(reopened.Exists);
            PlayerAuth loaded = reopened.LoadExisting(out bool migrated);
            Assert.False(migrated);
            Assert.Equal(firstProfile.Identity, loaded.Identity);
            Assert.Equal(firstProfile.IdentityPublic, loaded.IdentityPublic);
            Assert.Equal("Alice", loaded.Username);
            Assert.Equal(firstBytes, File.ReadAllBytes(firstPath));
            Assert.Equal("Bob", secondStore.LoadExisting(out _).Username);
            Assert.False(File.Exists(firstPath + ".bak"));
            Assert.False(File.Exists(firstPath + ".tmp"));
        }
        finally
        {
            Array.Clear(firstIdentity, 0, firstIdentity.Length);
            Array.Clear(secondIdentity, 0, secondIdentity.Length);
        }
    }

    [Fact]
    public void CorruptJsonLeavesOriginalBytesUnchanged()
    {
        byte[] original = Encoding.UTF8.GetBytes("{\r\n  \"identity\":");
        WriteOriginal(original);

        var store = CreateStore();

        Assert.ThrowsAny<Exception>(() => store.LoadExisting(out _));
        AssertOriginalAndNoArtifacts(original);
    }

    [Fact]
    public void DecryptionFailureLeavesOriginalBytesUnchanged()
    {
        var profile = new PlayerAuth
        {
            Identity = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
            IdentityProtection = PlayerAuth.DpapiProtection,
            Username = "Alice",
        };
        byte[] original = SerializeWithWhitespace(profile);
        WriteOriginal(original);
        var store = new PlayerAuthProfileStore(
            ProfilePath,
            Clone,
            _ => throw new InvalidOperationException("Injected identity decryption failure."),
            ReplaceFile);

        var error = Assert.Throws<InvalidOperationException>(() => store.LoadExisting(out _));

        Assert.Contains("decryption failure", error.Message);
        AssertOriginalAndNoArtifacts(original);
    }

    [Fact]
    public void ProtectionFailureDuringLegacyMigrationLeavesOriginalBytesUnchanged()
    {
        byte[] identity = AuthCrypto.CreateIdentity();
        try
        {
            byte[] original = SerializeWithWhitespace(new PlayerAuth
            {
                Identity = Convert.ToBase64String(identity),
                Username = "Alice",
            });
            WriteOriginal(original);
            var store = new PlayerAuthProfileStore(
                ProfilePath,
                _ => throw new InvalidOperationException("Injected identity protection failure."),
                Clone,
                ReplaceFile);

            var error = Assert.Throws<InvalidOperationException>(() => store.LoadExisting(out _));

            Assert.Contains("protection failure", error.Message);
            AssertOriginalAndNoArtifacts(original);
        }
        finally
        {
            Array.Clear(identity, 0, identity.Length);
        }
    }

    [Fact]
    public void InvalidRotationLineageLeavesOriginalBytesUnchanged()
    {
        byte[] identity = AuthCrypto.CreateIdentity();
        try
        {
            byte[] original = SerializeWithWhitespace(new PlayerAuth
            {
                Identity = Convert.ToBase64String(identity),
                Username = "Alice",
                IdentityRotationChain = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
            });
            WriteOriginal(original);
            var store = CreateStore();

            Assert.ThrowsAny<Exception>(() => store.LoadExisting(out _));

            AssertOriginalAndNoArtifacts(original);
        }
        finally
        {
            Array.Clear(identity, 0, identity.Length);
        }
    }

    [Fact]
    public void ValidUnchangedProfileIsNotRewritten()
    {
        byte[] identity = AuthCrypto.CreateIdentity();
        byte[] identityPublic = null;
        try
        {
            identityPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(identity));
            var profile = new PlayerAuth
            {
                Identity = Convert.ToBase64String(Transform(identity)),
                IdentityProtection = PlayerAuth.DpapiProtection,
                IdentityPublic = Convert.ToBase64String(identityPublic),
                Username = "Alice",
            };
            byte[] original = SerializeWithWhitespace(profile);
            WriteOriginal(original);
            var store = CreateStore();

            PlayerAuth loaded = store.LoadExisting(out bool migrated);

            Assert.False(migrated);
            Assert.Equal("Alice", loaded.Username);
            AssertOriginalAndNoArtifacts(original);
        }
        finally
        {
            Array.Clear(identity, 0, identity.Length);
            if (identityPublic != null)
                Array.Clear(identityPublic, 0, identityPublic.Length);
        }
    }

    [Fact]
    public void AtomicReplacementCreatesByteExactBackup()
    {
        byte[] original = Encoding.UTF8.GetBytes("{\r\n  \"old\": true\r\n}");
        WriteOriginal(original);
        var store = CreateStore();

        store.Save(new PlayerAuth
        {
            Identity = "replacement",
            IdentityProtection = PlayerAuth.DpapiProtection,
            Username = "Alice",
        });

        Assert.Equal(original, File.ReadAllBytes(ProfilePath + ".bak"));
        var saved = JsonConvert.DeserializeObject<PlayerAuth>(File.ReadAllText(ProfilePath));
        Assert.Equal("replacement", saved.Identity);
        Assert.False(File.Exists(ProfilePath + ".tmp"));
    }

    [Fact]
    public void ReplacementFailurePreservesOriginalAndCleansTemporaryFile()
    {
        byte[] original = Encoding.UTF8.GetBytes("{\"old\":true}");
        WriteOriginal(original);
        var store = new PlayerAuthProfileStore(
            ProfilePath,
            Transform,
            Transform,
            (_, _, _) => throw new IOException("Injected atomic replacement failure."));

        Assert.Throws<IOException>(() => store.Save(new PlayerAuth
        {
            Identity = "replacement",
            IdentityProtection = PlayerAuth.DpapiProtection,
        }));

        AssertOriginalAndNoArtifacts(original);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private PlayerAuthProfileStore CreateStore()
        => new(ProfilePath, Transform, Transform, ReplaceFile);

    private void WriteOriginal(byte[] bytes)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(ProfilePath, bytes);
    }

    private void AssertOriginalAndNoArtifacts(byte[] original)
    {
        Assert.Equal(original, File.ReadAllBytes(ProfilePath));
        Assert.False(File.Exists(ProfilePath + ".tmp"));
        Assert.False(File.Exists(ProfilePath + ".bak"));
    }

    private static byte[] SerializeWithWhitespace(PlayerAuth profile)
        => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(profile, Formatting.Indented));

    private static byte[] Clone(byte[] value)
        => (byte[])value.Clone();

    private static byte[] Transform(byte[] value)
        => value.Select(b => (byte)(b ^ 0x5a)).ToArray();

    private static void ReplaceFile(string source, string destination, string backup)
        => File.Replace(source, destination, backup, ignoreMetadataErrors: true);
}
