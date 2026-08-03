using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Anomaly.Shared.Auth;
using LabApi.Loader;

namespace Anomaly.Server.Auth;

/// <summary>Owns the persistent server signing identity used by protocol v2.</summary>
internal static class ServerIdentityStore
{
    private const string IdentityFileName = "server_identity";
    private const int EnrollmentSecretSize = 32;
    private const long RotationBackdateSeconds = 300;
    private const long RotationValiditySeconds = 180L * 24L * 60L * 60L;
    private static readonly object Sync = new object();
    private static byte[] _identity;
    private static byte[] _enrollmentSecret;
    private static ServerIdentityRotationRecord _rotationRecord;
    private static ulong _rotationSequence;

    public static string Fingerprint { get; private set; } = string.Empty;

    public static bool LoadOrCreate(out string error)
    {
        error = null;
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            error = "Anomaly plugin is not initialized.";
            return false;
        }

        lock (Sync)
        {
            try
            {
                var path = plugin.GetConfigPath(IdentityFileName);
                if (plugin.TryReadConfig(IdentityFileName, out ServerIdentityFile stored))
                {
                    if (!TryDecodeIdentity(stored, out _identity, out error))
                    {
                        ClearLoadedState();
                        return false;
                    }

                    bool migratedEnrollmentSecret = false;
                    if (string.IsNullOrWhiteSpace(stored.EnrollmentSecretBase64))
                    {
                        // Existing installs historically used the signing identity
                        // itself as the enrollment HMAC key. Persist that exact key
                        // once so future signing-key rotations do not rewrite every
                        // moderation identifier.
                        _enrollmentSecret = (byte[])_identity.Clone();
                        stored.EnrollmentSecretBase64 = Convert.ToBase64String(_enrollmentSecret);
                        migratedEnrollmentSecret = true;
                    }
                    else if (!TryDecodeEnrollmentSecret(stored, out _enrollmentSecret, out error))
                    {
                        ClearLoadedState();
                        return false;
                    }

                    if (!TryDecodeRotationRecord(stored, _identity, out _rotationRecord, out error))
                    {
                        ClearLoadedState();
                        return false;
                    }
                    _rotationSequence = stored.RotationSequence;

                    if (migratedEnrollmentSecret && !plugin.TrySaveConfig(stored, IdentityFileName))
                    {
                        error = "LabAPI failed to persist the stable enrollment secret migration.";
                        ClearLoadedState();
                        return false;
                    }
                }
                else
                {
                    if (File.Exists(path))
                    {
                        error = $"The persistent server identity at '{path}' exists but could not be decoded. Refusing to rotate it silently.";
                        return false;
                    }

                    _identity = AuthCrypto.CreateIdentity();
                    _enrollmentSecret = CreateRandomBytes(EnrollmentSecretSize);
                    // Validate before persisting and before advertising the public half.
                    AuthCrypto.ReadIdentity(_identity);
                    var created = new ServerIdentityFile
                    {
                        IdentityBase64 = Convert.ToBase64String(_identity),
                        EnrollmentSecretBase64 = Convert.ToBase64String(_enrollmentSecret),
                        CreatedUtc = DateTime.UtcNow.ToString("O"),
                    };
                    if (!plugin.TrySaveConfig(created, IdentityFileName))
                    {
                        Array.Clear(_identity, 0, _identity.Length);
                        Array.Clear(_enrollmentSecret, 0, _enrollmentSecret.Length);
                        _identity = null;
                        _enrollmentSecret = null;
                        error = "LabAPI failed to persist the server signing identity.";
                        return false;
                    }
                }

                var keyPair = AuthCrypto.ReadIdentity(_identity);
                var publicKey = AuthCrypto.WritePublic(keyPair);
                Fingerprint = ServerIdentityRotationRecord.FingerprintHex(publicKey);
                return true;
            }
            catch (Exception ex)
            {
                if (_identity != null)
                    Array.Clear(_identity, 0, _identity.Length);
                if (_enrollmentSecret != null)
                    Array.Clear(_enrollmentSecret, 0, _enrollmentSecret.Length);
                _identity = null;
                _enrollmentSecret = null;
                _rotationRecord = null;
                _rotationSequence = 0;
                Fingerprint = string.Empty;
                error = ex.Message;
                return false;
            }
        }
    }

    public static ServerIdentityRotationRecord GetActiveRotationRecord()
    {
        lock (Sync)
        {
            if (_rotationRecord == null)
                return null;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now < _rotationRecord.NotBeforeUnixSeconds || now > _rotationRecord.ExpiresUnixSeconds)
                return null;
            return _rotationRecord.Clone();
        }
    }

    public static bool TryRotate(out ServerIdentityRotationRecord rotationRecord, out string error)
    {
        rotationRecord = null;
        error = null;
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            error = "Anomaly plugin is not initialized.";
            return false;
        }

        lock (Sync)
        {
            if (_identity == null || _enrollmentSecret == null)
            {
                error = "Server identity has not been loaded.";
                return false;
            }

            byte[] nextIdentity = null;
            try
            {
                if (_rotationSequence == ulong.MaxValue)
                {
                    error = "Server identity rotation sequence is exhausted.";
                    return false;
                }

                nextIdentity = AuthCrypto.CreateIdentity();
                ulong nextSequence = _rotationSequence + 1;
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var nextRecord = ServerIdentityRotationRecord.Create(
                    _identity,
                    nextIdentity,
                    Math.Max(0, now - RotationBackdateSeconds),
                    checked(now + RotationValiditySeconds),
                    nextSequence);

                var persisted = new ServerIdentityFile
                {
                    IdentityBase64 = Convert.ToBase64String(nextIdentity),
                    EnrollmentSecretBase64 = Convert.ToBase64String(_enrollmentSecret),
                    CreatedUtc = DateTime.UtcNow.ToString("O"),
                    RotationRecordBase64 = Convert.ToBase64String(nextRecord.Encode()),
                    RotationSequence = nextSequence,
                };
                if (!plugin.TrySaveConfig(persisted, IdentityFileName))
                {
                    error = "LabAPI failed to persist the rotated signing identity.";
                    return false;
                }

                Array.Clear(_identity, 0, _identity.Length);
                _identity = nextIdentity;
                nextIdentity = null;
                _rotationRecord = nextRecord;
                _rotationSequence = nextSequence;
                Fingerprint = ServerIdentityRotationRecord.FingerprintHex(nextRecord.NewIdentityPublic);
                rotationRecord = nextRecord.Clone();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (nextIdentity != null)
                    Array.Clear(nextIdentity, 0, nextIdentity.Length);
            }
        }
    }

    public static byte[] GetIdentity()
    {
        lock (Sync)
        {
            if (_identity == null)
                throw new InvalidOperationException("Server signing identity has not been loaded.");
            return (byte[])_identity.Clone();
        }
    }

    /// <summary>
    /// Produces the opaque server-local enrollment identifier used by bans,
    /// allowlists, rate limits, and logs. The same global client public key maps to a
    /// different identifier on every independently keyed server.
    /// </summary>
    public static string GetEnrollmentId(byte[] clientIdentityPublic)
    {
        if (clientIdentityPublic == null || clientIdentityPublic.Length != ClientHandshake.IdentityPublicSize)
            throw new ArgumentException(
                $"Client identity public key must be {ClientHandshake.IdentityPublicSize} bytes.",
                nameof(clientIdentityPublic));

        lock (Sync)
        {
            if (_enrollmentSecret == null)
                throw new InvalidOperationException("Server enrollment secret has not been loaded.");

            var input = new byte[Encoding.ASCII.GetByteCount("ANOMALY-ENROLLMENT-V1") + clientIdentityPublic.Length];
            var prefixLength = Encoding.ASCII.GetBytes("ANOMALY-ENROLLMENT-V1", 0, "ANOMALY-ENROLLMENT-V1".Length, input, 0);
            Array.Copy(clientIdentityPublic, 0, input, prefixLength, clientIdentityPublic.Length);
            byte[] hash;
            using (var hmac = new HMACSHA256(_enrollmentSecret))
                hash = hmac.ComputeHash(input);
            return Base58.Encode(hash) + "@anomaly";
        }
    }

    public static void Reset()
    {
        lock (Sync)
            ClearLoadedState();
    }

    private static bool TryDecodeIdentity(ServerIdentityFile stored, out byte[] identity, out string error)
    {
        identity = null;
        error = null;
        if (stored == null || string.IsNullOrWhiteSpace(stored.IdentityBase64))
        {
            error = "Persistent server identity file is empty.";
            return false;
        }

        try
        {
            identity = Convert.FromBase64String(stored.IdentityBase64);
            AuthCrypto.ReadIdentity(identity);
            return true;
        }
        catch (Exception ex)
        {
            if (identity != null)
                Array.Clear(identity, 0, identity.Length);
            identity = null;
            error = "Persistent server identity is invalid: " + ex.Message;
            return false;
        }
    }

    private static bool TryDecodeEnrollmentSecret(
        ServerIdentityFile stored,
        out byte[] secret,
        out string error)
    {
        secret = null;
        error = null;
        try
        {
            secret = Convert.FromBase64String(stored.EnrollmentSecretBase64);
            if (secret.Length != EnrollmentSecretSize && secret.Length != 97)
            {
                Array.Clear(secret, 0, secret.Length);
                secret = null;
                error = "Persistent enrollment secret had an invalid length.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            if (secret != null)
                Array.Clear(secret, 0, secret.Length);
            secret = null;
            error = "Persistent enrollment secret is invalid: " + ex.Message;
            return false;
        }
    }

    private static bool TryDecodeRotationRecord(
        ServerIdentityFile stored,
        byte[] currentIdentity,
        out ServerIdentityRotationRecord record,
        out string error)
    {
        record = null;
        error = null;
        if (string.IsNullOrWhiteSpace(stored?.RotationRecordBase64))
        {
            if (stored?.RotationSequence != 0)
            {
                error = "Server identity has a rotation sequence but no signed rotation record.";
                return false;
            }
            return true;
        }

        try
        {
            record = ServerIdentityRotationRecord.Decode(
                Convert.FromBase64String(stored.RotationRecordBase64));
            if (record.Sequence != stored.RotationSequence)
            {
                error = "Persisted server rotation sequence did not match its signed record.";
                record = null;
                return false;
            }

            byte[] currentPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(currentIdentity));
            ulong priorSequence = record.Sequence - 1;
            if (!record.Validate(
                    record.OldIdentityPublic,
                    currentPublic,
                    record.NotBeforeUnixSeconds,
                    priorSequence,
                    out error))
            {
                record = null;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            record = null;
            error = "Persistent server rotation record is invalid: " + ex.Message;
            return false;
        }
    }

    private static byte[] CreateRandomBytes(int length)
    {
        var value = new byte[length];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(value);
        return value;
    }

    private static void ClearLoadedState()
    {
        if (_identity != null)
            Array.Clear(_identity, 0, _identity.Length);
        if (_enrollmentSecret != null)
            Array.Clear(_enrollmentSecret, 0, _enrollmentSecret.Length);
        _identity = null;
        _enrollmentSecret = null;
        _rotationRecord = null;
        _rotationSequence = 0;
        Fingerprint = string.Empty;
    }
}

public sealed class ServerIdentityFile
{
    public string IdentityBase64 { get; set; } = string.Empty;
    public string EnrollmentSecretBase64 { get; set; } = string.Empty;
    public string CreatedUtc { get; set; } = string.Empty;
    public string RotationRecordBase64 { get; set; } = string.Empty;
    public ulong RotationSequence { get; set; }
}
