using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Auth;
using LabApi.Loader;

namespace Anomaly.Server.Auth;

internal static class PasswordStore
{
    private const string PasswordFileName = "password";
    private static readonly object Sync = new object();
    private static readonly Dictionary<string, FailureState> Failures = new Dictionary<string, FailureState>();

    private static PasswordStorageFile _storage;

    public static bool IsRequired => Plugin.Instance?.Config?.RequirePassword == true;

    public static bool HasPassword
    {
        get
        {
            lock (Sync)
                return TryGetVerifierLocked(out _, out _, out _);
        }
    }

    public static void Load()
    {
        var plugin = Plugin.Instance;
        if (plugin == null)
            return;

        if (!plugin.TryReadConfig(PasswordFileName, out PasswordStorageFile storage))
        {
            lock (Sync)
                _storage = null;
            return;
        }

        lock (Sync)
            _storage = IsStorageValid(storage) ? storage : null;
    }

    public static void Reset()
    {
        lock (Sync)
        {
            _storage = null;
            Failures.Clear();
        }
    }

    public static bool SetPassword(string password, int iterations, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(password))
        {
            error = "Password cannot be empty.";
            return false;
        }

        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            error = "Anomaly plugin is not initialized.";
            return false;
        }

        if (iterations < PasswordAuth.MinimumIterations)
            iterations = PasswordAuth.MinimumIterations;

        var salt = PasswordAuth.GenerateSalt();
        var verifier = PasswordAuth.DeriveVerifier(password, salt, iterations);
        try
        {
            var storage = new PasswordStorageFile
            {
                SaltBase64 = Convert.ToBase64String(salt),
                VerifierBase64 = Convert.ToBase64String(verifier),
                Iterations = iterations,
                UpdatedUtc = DateTime.UtcNow.ToString("O"),
            };

            if (!plugin.TrySaveConfig(storage, PasswordFileName))
            {
                error = "LabAPI failed to save the password verifier file.";
                return false;
            }

            lock (Sync)
                _storage = storage;

            return true;
        }
        finally
        {
            Array.Clear(verifier, 0, verifier.Length);
        }
    }

    public static bool ClearPassword(out string error)
    {
        error = null;
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            error = "Anomaly plugin is not initialized.";
            return false;
        }

        try
        {
            var path = plugin.GetConfigPath(PasswordFileName);
            if (File.Exists(path))
                File.Delete(path);

            lock (Sync)
            {
                _storage = null;
                Failures.Clear();
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryCreateChallenge(out PasswordChallenge challenge, out string error)
    {
        challenge = PasswordChallenge.NotRequired();
        error = null;

        if (!IsRequired)
            return true;

        lock (Sync)
        {
            if (!TryGetVerifierLocked(out var salt, out _, out var iterations))
            {
                error = "Password authentication is required but no verifier is configured. Run 'password <value>' in LocalAdmin.";
                return false;
            }

            challenge = new PasswordChallenge
            {
                Required = true,
                Salt = salt,
                Iterations = iterations,
                Nonce = PasswordAuth.GenerateNonce(),
            };
            return true;
        }
    }

    public static bool VerifyProof(Connection connection, string serverIdentifier, byte[] proof)
    {
        if (connection == null || !connection.PasswordRequired)
            return true;
        if (proof == null || proof.Length != PasswordAuth.ProofSize)
            return false;
        if (connection.PasswordNonce == null || connection.PasswordNonce.Length != PasswordAuth.NonceSize)
            return false;
        if (connection.Handshake?.nonce == null || connection.Handshake.nonce.Length != 12)
            return false;

        byte[] verifier;
        lock (Sync)
        {
            if (!TryGetVerifierLocked(out _, out verifier, out _))
                return false;
        }

        var serverChallenge = connection.ServerEnv.CreateHandshake().challenge;
        var expected = PasswordAuth.CreateProof(
            verifier,
            connection.Handshake.nonce,
            serverChallenge,
            serverIdentifier,
            connection.PasswordNonce);

        return PasswordAuth.FixedTimeEquals(expected, proof);
    }

    public static bool IsBlocked(string ip)
    {
        if (string.IsNullOrEmpty(ip))
            return false;

        var config = Plugin.Instance?.Config;
        if (config == null || config.PasswordMaxFailuresPerIp <= 0 || config.PasswordBlockSeconds <= 0)
            return false;

        lock (Sync)
        {
            if (!Failures.TryGetValue(ip, out var state))
                return false;

            if (state.BlockedUntilUtc > DateTime.UtcNow)
                return true;

            if (state.BlockedUntilUtc != DateTime.MinValue)
                Failures.Remove(ip);

            return false;
        }
    }

    public static void RecordFailure(string ip)
    {
        if (string.IsNullOrEmpty(ip))
            return;

        var config = Plugin.Instance?.Config;
        if (config == null || config.PasswordMaxFailuresPerIp <= 0 || config.PasswordBlockSeconds <= 0)
            return;

        lock (Sync)
        {
            if (!Failures.TryGetValue(ip, out var state))
            {
                state = new FailureState();
                Failures[ip] = state;
            }

            state.Failures++;
            if (state.Failures >= config.PasswordMaxFailuresPerIp)
            {
                state.BlockedUntilUtc = DateTime.UtcNow.AddSeconds(config.PasswordBlockSeconds);
                state.Failures = 0;
            }
        }
    }

    public static void ClearFailures(string ip)
    {
        if (string.IsNullOrEmpty(ip))
            return;

        lock (Sync)
            Failures.Remove(ip);
    }

    private static bool TryGetVerifierLocked(out byte[] salt, out byte[] verifier, out int iterations)
    {
        salt = null;
        verifier = null;
        iterations = 0;

        if (!IsStorageValid(_storage))
            return false;

        salt = Convert.FromBase64String(_storage.SaltBase64);
        verifier = Convert.FromBase64String(_storage.VerifierBase64);
        iterations = _storage.Iterations;
        return true;
    }

    private static bool IsStorageValid(PasswordStorageFile storage)
    {
        if (storage == null || storage.Iterations < PasswordAuth.MinimumIterations)
            return false;

        try
        {
            return Convert.FromBase64String(storage.SaltBase64).Length == PasswordAuth.SaltSize
                && Convert.FromBase64String(storage.VerifierBase64).Length == PasswordAuth.VerifierSize;
        }
        catch
        {
            return false;
        }
    }

    private sealed class FailureState
    {
        public int Failures;
        public DateTime BlockedUntilUtc;
    }
}

public class PasswordStorageFile
{
    public string SaltBase64 { get; set; } = string.Empty;
    public string VerifierBase64 { get; set; } = string.Empty;
    public int Iterations { get; set; } = PasswordAuth.DefaultIterations;
    public string UpdatedUtc { get; set; } = string.Empty;
}
