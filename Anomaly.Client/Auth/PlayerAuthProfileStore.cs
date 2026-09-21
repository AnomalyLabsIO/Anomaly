using System;
using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Shared.Auth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Anomaly.Client.Auth;

/// <summary>
/// Owns the durable client identity profile boundary. Existing bytes are only
/// replaced after JSON parsing, private-key unprotection, lineage validation,
/// and public-key synchronization all succeed.
/// </summary>
internal sealed class PlayerAuthProfileStore
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private readonly string _profilePath;
    private readonly Func<byte[], byte[]> _protect;
    private readonly Func<byte[], byte[]> _unprotect;
    private readonly Action<string, string, string> _replaceExisting;
    private readonly object _writeLock = new();

    internal PlayerAuthProfileStore(string profilePath)
        : this(
            profilePath,
            WindowsDataProtection.Protect,
            WindowsDataProtection.Unprotect,
            (source, destination, backup) =>
                File.Replace(source, destination, backup, ignoreMetadataErrors: true))
    {
    }

    internal PlayerAuthProfileStore(
        string profilePath,
        Func<byte[], byte[]> protect,
        Func<byte[], byte[]> unprotect,
        Action<string, string, string> replaceExisting)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            throw new ArgumentException("A client identity profile path is required.", nameof(profilePath));

        _profilePath = profilePath;
        _protect = protect ?? throw new ArgumentNullException(nameof(protect));
        _unprotect = unprotect ?? throw new ArgumentNullException(nameof(unprotect));
        _replaceExisting = replaceExisting ?? throw new ArgumentNullException(nameof(replaceExisting));
    }

    internal bool Exists => File.Exists(_profilePath);

    internal PlayerAuth LoadExisting(out bool migrated)
    {
        byte[] originalBytes = File.ReadAllBytes(_profilePath);
        var prepared = PrepareExisting(originalBytes);
        migrated = prepared.RequiresWrite;
        if (prepared.RequiresWrite)
            Save(prepared.Profile);
        return prepared.Profile;
    }

    internal void Save(PlayerAuth profile)
    {
        byte[] bytes = StrictUtf8.GetBytes(JsonConvert.SerializeObject(profile, Formatting.None));
        string directory = Path.GetDirectoryName(_profilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = _profilePath + ".tmp";
        string backupPath = _profilePath + ".bak";
        lock (_writeLock)
        {
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(_profilePath))
                    _replaceExisting(tempPath, _profilePath, backupPath);
                else
                    File.Move(tempPath, _profilePath);
            }
            catch
            {
                TryDeleteTemporaryFile(tempPath);
                throw;
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
    }

    private PreparedPlayerAuth PrepareExisting(byte[] serialized)
    {
        var auth = DeserializeStrict(serialized);
        if (string.IsNullOrWhiteSpace(auth.Identity) || auth.Identity == "pending")
            throw new InvalidDataException("The client identity profile does not contain a usable identity.");
        if (auth.Username != null
            && (auth.Username.Length > 32 || auth.Username.Any(char.IsControl)))
            throw new InvalidDataException("The stored nickname is invalid.");

        ValidatePrivateIdentity(auth);

        bool requiresWrite = false;
        if (string.IsNullOrEmpty(auth.IdentityProtection))
        {
            byte[] legacyIdentity = auth.GetIdentity(_unprotect);
            try
            {
                auth.SetIdentity(legacyIdentity, _protect);
            }
            finally
            {
                Array.Clear(legacyIdentity, 0, legacyIdentity.Length);
            }
            requiresWrite = true;
        }

        ValidateStoredIdentityLineage(auth);
        requiresWrite |= SynchronizeIdentityPublic(ref auth);
        return new PreparedPlayerAuth(auth, requiresWrite);
    }

    private static PlayerAuth DeserializeStrict(byte[] serialized)
    {
        string json = StrictUtf8.GetString(serialized ?? throw new ArgumentNullException(nameof(serialized)));
        using (var textReader = new StringReader(json))
        using (var jsonReader = new JsonTextReader(textReader)
               {
                   DateParseHandling = DateParseHandling.None,
               })
        {
            var root = JObject.Load(
                jsonReader,
                new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                });
            while (jsonReader.Read())
            {
                if (jsonReader.TokenType != JsonToken.Comment)
                    throw new JsonSerializationException("The client identity profile contains trailing JSON content.");
            }
            return root.ToObject<PlayerAuth>();
        }
    }

    private void ValidatePrivateIdentity(PlayerAuth auth)
    {
        byte[] identity = auth.GetIdentity(_unprotect);
        try
        {
            // Parsing proves the private key is structurally valid before any
            // migration or additive profile write is attempted.
            AuthCrypto.ReadIdentity(identity);
        }
        finally
        {
            Array.Clear(identity, 0, identity.Length);
        }
    }

    private void ValidateStoredIdentityLineage(PlayerAuth auth)
    {
        var chain = auth.GetIdentityRotationChain();
        if (chain == null)
            return;

        byte[] identity = auth.GetIdentity(_unprotect);
        byte[] currentPublic = null;
        try
        {
            currentPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(identity));
            if (!chain.TryValidate(currentPublic, out _, out var error))
                throw new InvalidDataException("Stored client identity rotation chain is invalid: " + error);
        }
        finally
        {
            Array.Clear(identity, 0, identity.Length);
            if (currentPublic != null)
                Array.Clear(currentPublic, 0, currentPublic.Length);
        }
    }

    private bool SynchronizeIdentityPublic(ref PlayerAuth auth)
    {
        byte[] identity = auth.GetIdentity(_unprotect);
        byte[] currentPublic = null;
        try
        {
            currentPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(identity));
            string encodedPublic = Convert.ToBase64String(currentPublic);
            if (string.IsNullOrWhiteSpace(auth.IdentityPublic))
            {
                auth.IdentityPublic = encodedPublic;
                return true;
            }

            byte[] storedPublic = Convert.FromBase64String(auth.IdentityPublic);
            try
            {
                if (storedPublic.Length != ClientHandshake.IdentityPublicSize
                    || !PasswordAuth.FixedTimeEquals(storedPublic, currentPublic))
                    throw new InvalidDataException(
                        "Stored identity_public does not match the protected private identity.");
            }
            finally
            {
                Array.Clear(storedPublic, 0, storedPublic.Length);
            }
            return false;
        }
        finally
        {
            Array.Clear(identity, 0, identity.Length);
            if (currentPublic != null)
                Array.Clear(currentPublic, 0, currentPublic.Length);
        }
    }

    private static void TryDeleteTemporaryFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // The durable identity remains the previous file. A stale
            // temporary file is never read by the authentication path.
        }
    }

    private readonly struct PreparedPlayerAuth
    {
        internal PreparedPlayerAuth(PlayerAuth profile, bool requiresWrite)
        {
            Profile = profile;
            RequiresWrite = requiresWrite;
        }

        internal PlayerAuth Profile { get; }
        internal bool RequiresWrite { get; }
    }
}
