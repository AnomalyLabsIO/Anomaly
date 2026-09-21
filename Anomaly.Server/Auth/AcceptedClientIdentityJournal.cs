using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using Anomaly.Shared.Auth;

namespace Anomaly.Server.Auth;

/// <summary>
/// Durable, fail-closed record of the last client identity leaf accepted for each
/// server-local enrollment. The journal is intentionally independent of LabAPI so
/// the validation and atomic persistence boundary can be exercised in tests.
/// </summary>
internal sealed class AcceptedClientIdentityJournal
{
    private const int SchemaVersion = 1;
    private const int MaximumEntries = 100000;
    private const long MaximumJournalBytes = 64L * 1024L * 1024L;

    private readonly object _sync = new object();
    private readonly string _path;
    private Dictionary<string, AcceptedClientIdentityRecord> _records =
        new Dictionary<string, AcceptedClientIdentityRecord>(StringComparer.Ordinal);

    public AcceptedClientIdentityJournal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Accepted-client identity journal path is required.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    internal int Count
    {
        get
        {
            lock (_sync)
                return _records.Count;
        }
    }

    public bool TryLoad(out string error)
    {
        error = null;
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    if (File.Exists(_path + ".bak"))
                        throw new InvalidDataException(
                            "Primary journal is missing while its recovery backup exists; operator recovery is required.");
                    _records = new Dictionary<string, AcceptedClientIdentityRecord>(StringComparer.Ordinal);
                    return true;
                }

                var info = new FileInfo(_path);
                if (info.Length <= 0 || info.Length > MaximumJournalBytes)
                    throw new InvalidDataException("Accepted-client identity journal size was invalid.");

                var serialized = File.ReadAllBytes(_path);
                AcceptedClientIdentityFile persisted;
                using (var stream = new MemoryStream(serialized, false))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AcceptedClientIdentityFile));
                    persisted = serializer.ReadObject(stream) as AcceptedClientIdentityFile;
                }
                if (persisted == null || persisted.Schema != SchemaVersion || persisted.Clients == null)
                    throw new InvalidDataException("Accepted-client identity journal schema was invalid.");
                if (persisted.Clients.Count > MaximumEntries)
                    throw new InvalidDataException("Accepted-client identity journal exceeded its entry ceiling.");

                var loaded = new Dictionary<string, AcceptedClientIdentityRecord>(StringComparer.Ordinal);
                foreach (var record in persisted.Clients)
                {
                    ValidatePersistedRecord(record);
                    if (loaded.ContainsKey(record.EnrollmentId))
                        throw new InvalidDataException("Accepted-client identity journal contained a duplicate enrollment ID.");
                    loaded.Add(record.EnrollmentId, record.Clone());
                }

                _records = loaded;
                return true;
            }
            catch (Exception ex)
            {
                error = "Accepted-client identity journal could not be loaded safely: " + ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// Validates an already-authenticated handshake against the durable high-water
    /// mark and persists an advance before returning success.
    /// </summary>
    public bool TryValidateAndCommit(
        string enrollmentId,
        ClientHandshake handshake,
        out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(enrollmentId) || handshake == null)
        {
            error = "Enrollment ID and client handshake are required.";
            return false;
        }

        byte[] enrollmentIdentity = null;
        try
        {
            if (!handshake.TryValidateIdentityRotationChain(out enrollmentIdentity, out error))
            {
                error = "Client identity lineage was invalid: " + error;
                return false;
            }

            var currentIdentity = handshake.identityPublic;
            if (currentIdentity == null || currentIdentity.Length != ClientHandshake.IdentityPublicSize)
            {
                error = "Client current identity public key was invalid.";
                return false;
            }

            var candidateSequence = (ulong)(handshake.identityRotationChain?.Records?.Count ?? 0);
            var candidateFingerprint = Fingerprint(currentIdentity);

            lock (_sync)
            {
                if (_records.TryGetValue(enrollmentId, out var accepted))
                {
                    var acceptedIdentity = Convert.FromBase64String(accepted.CurrentIdentityPublicBase64);
                    try
                    {
                        if (candidateSequence < accepted.Sequence)
                        {
                            error = $"Client identity rollback rejected (accepted={accepted.Sequence}, candidate={candidateSequence}).";
                            return false;
                        }

                        if (candidateSequence == accepted.Sequence)
                        {
                            if (!PasswordAuth.FixedTimeEquals(acceptedIdentity, currentIdentity))
                            {
                                error = $"Client identity fork rejected at sequence {candidateSequence}.";
                                return false;
                            }
                            return true;
                        }

                        var chain = handshake.identityRotationChain;
                        if (chain?.Records == null || candidateSequence <= accepted.Sequence)
                        {
                            error = "Client identity advance did not contain a rotation chain.";
                            return false;
                        }

                        byte[] leafAtAcceptedSequence = accepted.Sequence == 0
                            ? enrollmentIdentity
                            : chain.Records[checked((int)accepted.Sequence - 1)].NewIdentityPublic;
                        if (!PasswordAuth.FixedTimeEquals(acceptedIdentity, leafAtAcceptedSequence))
                        {
                            error = $"Client identity advance did not contain the accepted leaf at sequence {accepted.Sequence}.";
                            return false;
                        }
                    }
                    finally
                    {
                        Array.Clear(acceptedIdentity, 0, acceptedIdentity.Length);
                    }
                }

                var next = CloneRecords(_records);
                next[enrollmentId] = new AcceptedClientIdentityRecord
                {
                    EnrollmentId = enrollmentId,
                    CurrentIdentityPublicBase64 = Convert.ToBase64String(currentIdentity),
                    CurrentIdentityFingerprint = candidateFingerprint,
                    Sequence = candidateSequence,
                };

                if (!TryPersist(next, out error))
                    return false;

                _records = next;
                return true;
            }
        }
        catch (Exception ex)
        {
            error = "Client identity acceptance failed: " + ex.Message;
            return false;
        }
        finally
        {
            if (enrollmentIdentity != null)
                Array.Clear(enrollmentIdentity, 0, enrollmentIdentity.Length);
        }
    }

    private bool TryPersist(
        Dictionary<string, AcceptedClientIdentityRecord> records,
        out string error)
    {
        error = null;
        var directory = Path.GetDirectoryName(_path);
        var tempPath = _path + ".tmp." + Guid.NewGuid().ToString("N");
        var backupPath = _path + ".bak";
        try
        {
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var file = new AcceptedClientIdentityFile();
            foreach (var record in records.Values)
                file.Clients.Add(record.Clone());
            file.Clients.Sort((left, right) => string.CompareOrdinal(left.EnrollmentId, right.EnrollmentId));

            byte[] serialized;
            using (var buffer = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(AcceptedClientIdentityFile));
                serializer.WriteObject(buffer, file);
                serialized = buffer.ToArray();
            }
            if (serialized.Length > MaximumJournalBytes)
                throw new InvalidDataException("Accepted-client identity journal exceeded its byte ceiling.");

            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(serialized, 0, serialized.Length);
                stream.Flush(true);
            }

            if (File.Exists(_path))
                File.Replace(tempPath, _path, backupPath, true);
            else
                File.Move(tempPath, _path);
            return true;
        }
        catch (Exception ex)
        {
            error = "Accepted-client identity journal could not be persisted atomically: " + ex.Message;
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // A same-directory temporary file is harmless and will never be read.
            }
        }
    }

    private static void ValidatePersistedRecord(AcceptedClientIdentityRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.EnrollmentId))
            throw new InvalidDataException("Accepted-client identity record had no enrollment ID.");
        if (record.Sequence > ClientIdentityRotationChain.MaximumLinks)
            throw new InvalidDataException("Accepted-client identity sequence exceeded the supported chain length.");

        byte[] identity = null;
        try
        {
            identity = Convert.FromBase64String(record.CurrentIdentityPublicBase64 ?? string.Empty);
            if (identity.Length != ClientHandshake.IdentityPublicSize)
                throw new InvalidDataException("Accepted-client identity public key length was invalid.");
            AuthCrypto.ReadPublic(identity);
            if (!string.Equals(Fingerprint(identity), record.CurrentIdentityFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("Accepted-client identity fingerprint did not match its public key.");
        }
        finally
        {
            if (identity != null)
                Array.Clear(identity, 0, identity.Length);
        }
    }

    private static Dictionary<string, AcceptedClientIdentityRecord> CloneRecords(
        Dictionary<string, AcceptedClientIdentityRecord> records)
    {
        var clone = new Dictionary<string, AcceptedClientIdentityRecord>(StringComparer.Ordinal);
        foreach (var pair in records)
            clone.Add(pair.Key, pair.Value.Clone());
        return clone;
    }

    private static string Fingerprint(byte[] identityPublic)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(identityPublic)).Replace("-", string.Empty).ToLowerInvariant();
    }

    [DataContract]
    private sealed class AcceptedClientIdentityFile
    {
        [DataMember(Name = "schema", Order = 1)]
        public int Schema { get; set; } = SchemaVersion;

        [DataMember(Name = "clients", Order = 2)]
        public List<AcceptedClientIdentityRecord> Clients { get; set; } =
            new List<AcceptedClientIdentityRecord>();
    }

    [DataContract]
    private sealed class AcceptedClientIdentityRecord
    {
        [DataMember(Name = "enrollment_id", Order = 1)]
        public string EnrollmentId { get; set; } = string.Empty;

        [DataMember(Name = "current_identity_public", Order = 2)]
        public string CurrentIdentityPublicBase64 { get; set; } = string.Empty;

        [DataMember(Name = "current_identity_fingerprint", Order = 3)]
        public string CurrentIdentityFingerprint { get; set; } = string.Empty;

        [DataMember(Name = "sequence", Order = 4)]
        public ulong Sequence { get; set; }

        public AcceptedClientIdentityRecord Clone()
        {
            return new AcceptedClientIdentityRecord
            {
                EnrollmentId = EnrollmentId,
                CurrentIdentityPublicBase64 = CurrentIdentityPublicBase64,
                CurrentIdentityFingerprint = CurrentIdentityFingerprint,
                Sequence = Sequence,
            };
        }
    }
}
