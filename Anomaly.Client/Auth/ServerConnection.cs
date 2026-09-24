using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Client.Api.Features;
using Anomaly.Shared.Auth;
using MelonLoader;
using Newtonsoft.Json;

namespace Anomaly.Client.Auth;

public class ServerConnection
{
    public ClientAuthSession Session { get; set; }
    public ServerHandshake Handshake { get; set; }
    public string ServerIdentifier { get; set; }
    public PasswordChallenge PasswordChallenge { get; set; }
    public byte[] PasswordProof { get; set; }
    public byte[] RetryCookie { get; set; }
    public string ServerFingerprint { get; set; }
    internal bool ModInventoryBound { get; set; }
}

internal enum ServerTrustState
{
    Trusted,
    FirstSeen,
    Changed,
    Unavailable,
}

/// <summary>
/// Endpoint-keyed TOFU pin store. First-seen keys are not persisted until the
/// player explicitly accepts the fingerprint shown in the in-game prompt.
/// </summary>
internal static class ServerTrustStore
{
    private const string FileName = "server-trust.json";
    private const int SchemaVersion = 2;
    private static readonly object Sync = new object();
    private static Dictionary<string, ServerTrustPin> _pins =
        new Dictionary<string, ServerTrustPin>(StringComparer.OrdinalIgnoreCase);
    private static string _path;
    private static string _loadError;

    public static void Load()
    {
        lock (Sync)
        {
            _path = Path.Combine(Paths.AnomalyPath, FileName);
            _pins = new Dictionary<string, ServerTrustPin>(StringComparer.OrdinalIgnoreCase);
            _loadError = null;
            if (!File.Exists(_path))
                return;

            string json;
            try
            {
                json = File.ReadAllText(_path);
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
                MelonLogger.Error(
                    $"Server trust store could not be read; server authentication will fail closed: {ex}");
                throw new InvalidDataException("Server trust store could not be read.", ex);
            }
            try
            {
                LoadCurrentDocument(json);
            }
            catch (Exception currentError)
            {
                try
                {
                    LoadLegacyDocument(json);
                    MelonLogger.Msg("Loaded the legacy fingerprint-only server trust store; verified keys will be upgraded in place.");
                }
                catch (Exception legacyError)
                {
                    _loadError = currentError.Message + " Legacy decode also failed: " + legacyError.Message;
                    _pins.Clear();
                    MelonLogger.Error(
                        $"Server trust store could not be loaded; server authentication will fail closed: {_loadError}");
                    throw new InvalidDataException("Server trust store could not be decoded.", currentError);
                }
            }
        }
    }

    public static ServerTrustState Evaluate(string endpoint, string fingerprint, out string expectedFingerprint)
    {
        lock (Sync)
        {
            expectedFingerprint = null;
            if (_loadError != null || string.IsNullOrWhiteSpace(endpoint) || !IsFingerprint(fingerprint))
                return ServerTrustState.Unavailable;
            if (!_pins.TryGetValue(endpoint, out var pin))
                return ServerTrustState.FirstSeen;
            expectedFingerprint = pin.Fingerprint;
            return string.Equals(expectedFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)
                ? ServerTrustState.Trusted
                : ServerTrustState.Changed;
        }
    }

    public static bool TryPinFirstSeen(
        string endpoint,
        string fingerprint,
        byte[] identityPublic,
        out string error)
    {
        error = null;
        lock (Sync)
        {
            if (_loadError != null)
            {
                error = "Trust store is unavailable: " + _loadError;
                return false;
            }
            if (!TryCreatePin(fingerprint, identityPublic, 0, out var pin, out error)
                || string.IsNullOrWhiteSpace(endpoint))
            {
                error = error ?? "Endpoint is invalid.";
                return false;
            }
            if (_pins.TryGetValue(endpoint, out var existing))
            {
                if (string.Equals(existing.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                    return true;
                error = "The endpoint is already pinned to a different key.";
                return false;
            }

            var next = ClonePins();
            next.Add(endpoint, pin);
            return TryCommitPins(next, out error);
        }
    }

    /// <summary>
    /// Adds the full public key to a fingerprint-only legacy pin. The matching
    /// fingerprint already authenticates these bytes, so no new trust decision is
    /// introduced by this migration.
    /// </summary>
    public static bool TryRememberTrustedIdentity(
        string endpoint,
        string fingerprint,
        byte[] identityPublic,
        out string error)
    {
        error = null;
        lock (Sync)
        {
            if (_loadError != null)
            {
                error = "Trust store is unavailable: " + _loadError;
                return false;
            }
            if (!_pins.TryGetValue(endpoint, out var existing)
                || !string.Equals(existing.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                error = "The endpoint is not pinned to the authenticated fingerprint.";
                return false;
            }
            if (!TryCreatePin(fingerprint, identityPublic, existing.RotationSequence, out var verified, out error))
                return false;

            if (!string.IsNullOrWhiteSpace(existing.IdentityPublicBase64))
            {
                if (!string.Equals(existing.IdentityPublicBase64, verified.IdentityPublicBase64, StringComparison.Ordinal))
                {
                    error = "The stored public key did not match the authenticated fingerprint.";
                    return false;
                }
                return true;
            }

            var next = ClonePins();
            next[endpoint] = verified;
            return TryCommitPins(next, out error);
        }
    }

    public static bool TryApplySignedRotation(
        string endpoint,
        byte[] advertisedNewIdentityPublic,
        ServerIdentityRotationRecord rotationRecord,
        long nowUnixSeconds,
        out string error)
    {
        error = null;
        lock (Sync)
        {
            if (_loadError != null)
            {
                error = "Trust store is unavailable: " + _loadError;
                return false;
            }
            if (rotationRecord == null)
            {
                error = "The server did not provide an old-key-signed rotation record.";
                return false;
            }
            if (!_pins.TryGetValue(endpoint, out var existing))
            {
                error = "The endpoint has no existing pin to authorize rotation.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(existing.IdentityPublicBase64))
            {
                error = "The legacy pin does not contain the old public key; reconnect once with the pinned key or explicitly replace it.";
                return false;
            }

            byte[] oldPublic;
            try
            {
                oldPublic = Convert.FromBase64String(existing.IdentityPublicBase64);
            }
            catch (Exception ex)
            {
                error = "The stored public key is invalid: " + ex.Message;
                return false;
            }

            if (!rotationRecord.Validate(
                    oldPublic,
                    advertisedNewIdentityPublic,
                    nowUnixSeconds,
                    existing.RotationSequence,
                    out error))
                return false;
            if (!string.Equals(
                    existing.Fingerprint,
                    ServerIdentityRotationRecord.FingerprintHex(rotationRecord.OldIdentityPublic),
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "Rotation proof old fingerprint did not match the stored pin.";
                return false;
            }

            string newFingerprint = ServerIdentityRotationRecord.FingerprintHex(advertisedNewIdentityPublic);
            if (!TryCreatePin(
                    newFingerprint,
                    advertisedNewIdentityPublic,
                    rotationRecord.Sequence,
                    out var replacement,
                    out error))
                return false;

            var next = ClonePins();
            next[endpoint] = replacement;
            return TryCommitPins(next, out error);
        }
    }

    public static string FormatFingerprint(string fingerprint)
    {
        if (!IsFingerprint(fingerprint))
            return fingerprint ?? string.Empty;
        var groups = new string[8];
        for (var i = 0; i < groups.Length; i++)
            groups[i] = fingerprint.Substring(i * 8, 8);
        return string.Join(" ", groups);
    }

    private static bool IsFingerprint(string value)
    {
        if (value == null || value.Length != 64)
            return false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }

    private static void LoadCurrentDocument(string json)
    {
        var document = JsonConvert.DeserializeObject<ServerTrustDocument>(json);
        if (document?.SchemaVersion != SchemaVersion || document.Pins == null)
            throw new InvalidDataException("Trust document schema or pins object was invalid.");

        var loaded = new Dictionary<string, ServerTrustPin>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in document.Pins)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                throw new InvalidDataException("Trust document contains an empty endpoint.");
            if (!TryValidatePin(pair.Value, false, out var error))
                throw new InvalidDataException("Trust document contains an invalid pin: " + error);
            loaded.Add(pair.Key, pair.Value.Clone());
        }
        _pins = loaded;
    }

    private static void LoadLegacyDocument(string json)
    {
        var document = JsonConvert.DeserializeObject<LegacyServerTrustDocument>(json);
        if (document?.Pins == null)
            throw new InvalidDataException("Legacy trust document has no pins object.");

        var loaded = new Dictionary<string, ServerTrustPin>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in document.Pins)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || !IsFingerprint(pair.Value))
                throw new InvalidDataException("Legacy trust document contains an invalid endpoint or fingerprint.");
            loaded.Add(pair.Key, new ServerTrustPin { Fingerprint = pair.Value.ToUpperInvariant() });
        }
        _pins = loaded;
    }

    private static bool TryCreatePin(
        string fingerprint,
        byte[] identityPublic,
        ulong rotationSequence,
        out ServerTrustPin pin,
        out string error)
    {
        pin = null;
        error = null;
        try
        {
            if (!IsFingerprint(fingerprint))
            {
                error = "Fingerprint is invalid.";
                return false;
            }
            if (identityPublic == null || identityPublic.Length != ClientHandshake.IdentityPublicSize)
            {
                error = $"Server public key must be {ClientHandshake.IdentityPublicSize} bytes.";
                return false;
            }
            string computed = ServerIdentityRotationRecord.FingerprintHex(identityPublic);
            if (!string.Equals(computed, fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                error = "Server public key did not match its fingerprint.";
                return false;
            }

            pin = new ServerTrustPin
            {
                Fingerprint = computed,
                IdentityPublicBase64 = Convert.ToBase64String(identityPublic),
                RotationSequence = rotationSequence,
            };
            return true;
        }
        catch (Exception ex)
        {
            error = "Server public key was invalid: " + ex.Message;
            return false;
        }
    }

    private static bool TryValidatePin(ServerTrustPin pin, bool requireIdentity, out string error)
    {
        error = null;
        if (pin == null || !IsFingerprint(pin.Fingerprint))
        {
            error = "Fingerprint is invalid.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(pin.IdentityPublicBase64))
        {
            if (requireIdentity || pin.RotationSequence != 0)
            {
                error = "Pin lacks the public key required by its rotation state.";
                return false;
            }
            return true;
        }

        try
        {
            byte[] identityPublic = Convert.FromBase64String(pin.IdentityPublicBase64);
            string computed = ServerIdentityRotationRecord.FingerprintHex(identityPublic);
            if (!string.Equals(computed, pin.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                error = "Stored public key did not match its fingerprint.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = "Stored public key was invalid: " + ex.Message;
            return false;
        }
    }

    private static Dictionary<string, ServerTrustPin> ClonePins()
    {
        var clone = new Dictionary<string, ServerTrustPin>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _pins)
            clone.Add(pair.Key, pair.Value.Clone());
        return clone;
    }

    private static bool TryCommitPins(Dictionary<string, ServerTrustPin> next, out string error)
    {
        error = null;
        string tempPath = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var document = new ServerTrustDocument { Pins = next };
            File.WriteAllText(tempPath, JsonConvert.SerializeObject(document, Formatting.Indented));
            if (File.Exists(_path))
                File.Replace(tempPath, _path, null);
            else
                File.Move(tempPath, _path);
            _pins = next;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // The authenticated in-memory pin remains unchanged. A stale
                // temporary file is ignored on the next load.
            }
            return false;
        }
    }

    private sealed class ServerTrustDocument
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = ServerTrustStore.SchemaVersion;

        [JsonProperty("pins")]
        public Dictionary<string, ServerTrustPin> Pins { get; set; }
    }

    private sealed class LegacyServerTrustDocument
    {
        [JsonProperty("pins")]
        public Dictionary<string, string> Pins { get; set; }
    }

    private sealed class ServerTrustPin
    {
        [JsonProperty("fingerprint")]
        public string Fingerprint { get; set; }

        [JsonProperty("identityPublic")]
        public string IdentityPublicBase64 { get; set; }

        [JsonProperty("rotationSequence")]
        public ulong RotationSequence { get; set; }

        public ServerTrustPin Clone() => new ServerTrustPin
        {
            Fingerprint = Fingerprint,
            IdentityPublicBase64 = IdentityPublicBase64,
            RotationSequence = RotationSequence,
        };
    }
}
