using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Il2Cpp;
using MelonLoader;
using System.Xml.Serialization;
using System.Reflection.Metadata;
using Il2CppLiteNetLib.Utils;
using Il2CppLiteNetLib;
using System.Collections;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Components;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Il2CppMEC;
using Newtonsoft.Json;
using UnityEngine;
using Action = Il2CppSystem.Action;

namespace Anomaly.Client.Auth;

public static class AuthHandler
{
    private const byte HandshakeResponseCode = 100;
    private const byte RetryCookieResponseCode = 101;
    private static Dictionary<string, ServerConnection> _connections = new();
    
    public static bool UsingAnomalyAuth { get; internal set; }

    public static string AuthFilePath { get; private set; }
    public static PlayerAuth PlayerAuth { get; private set; }

    internal static void Init()
    {
        try
        {
            AuthFilePath = Path.Combine(Paths.AnomalyPath, "user.json");
            ServerTrustStore.Load();
            if (!File.Exists(AuthFilePath))
                CreateNew();

            var auth = File.Exists(AuthFilePath) ? JsonConvert.DeserializeObject<PlayerAuth>(File.ReadAllText(AuthFilePath)) : CreateNew();

            if (string.IsNullOrEmpty(auth.Identity) || auth.Identity == "pending")
            {
                auth = CreateNew(auth.Username);
            }

            if (string.IsNullOrEmpty(auth.IdentityProtection))
            {
                var legacyIdentity = auth.GetIdentity();
                try
                {
                    auth.SetIdentity(legacyIdentity);
                    SavePlayerAuth(auth);
                    MelonLogger.Msg("Migrated the client identity from plaintext storage to Windows DPAPI current-user protection.");
                }
                finally
                {
                    Array.Clear(legacyIdentity, 0, legacyIdentity.Length);
                }
            }

            ValidateStoredIdentityLineage(auth);

            PlayerAuth = auth;
            MelonLogger.Msg(
                $"Auth initialized. Nickname selected: {!string.IsNullOrWhiteSpace(auth.Username)}, " +
                $"protected identity present: {!string.IsNullOrWhiteSpace(auth.Identity)}.");
            if (string.IsNullOrWhiteSpace(auth.Username))
                PromptForFirstRunNickname();
        }
        catch(Exception ex)
        {
            MelonLogger.Error("Failed to initialize auth: " + ex);
        }
    }

    private static bool _authWriteCalled;
    private static bool _phase2Sent;
    private static int _connectionGeneration;

    internal static void AuthWrite(NetDataWriter writer)
    {
        try
        {
            MelonLogger.Msg("AuthWrite called");
            var server = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.clientAddress;

            if (string.IsNullOrWhiteSpace(PlayerAuth.Username))
            {
                PromptForFirstRunNickname();
                MelonLogger.Warning("Authentication is paused until a nickname is selected.");
                return;
            }

            // Dedup: LiteNetLib invokes GetConnectData twice per logical connect
            // (~30ms apart, likely IPv4+IPv6 or internal retry) and IL2CPP adds
            // its own vtable-dispatch duplicates. Second+ calls must be no-ops
            // so the writer keeps the phase data written by the first call.
            if (_authWriteCalled)
            {
                MelonLogger.Warning("AuthWrite called again for same connection — ignoring duplicate GetConnectData call");
                return;
            }
            _authWriteCalled = true;

            var isSecondTime = false;

            if (_connections.TryGetValue(server, out var connection))
            {
                if (connection.Handshake != null)
                {
                    isSecondTime = true;
                }
                else if (connection.RetryCookie == null)
                {
                    _connections.Remove(server);
                }
            }

            //Writes the Game Version
            writer.Put((byte)(isSecondTime ? 4 : 3));
            writer.Put(Il2CppGameCore.Version.Major);
            writer.Put(Il2CppGameCore.Version.Minor);
            writer.Put(Il2CppGameCore.Version.Revision);
            writer.Put(Il2CppGameCore.Version.BackwardCompatibility);
            if (Il2CppGameCore.Version.BackwardCompatibility)
                writer.Put(Il2CppGameCore.Version.BackwardRevision);

            //Writes how long this Request is valid
            writer.Put(TimeBehaviour.CurrentUnixTimestamp + 30);

            MelonLogger.Msg($"AuthWrite phase: {(isSecondTime ? "2 (login)" : "1 (handshake)")}, server: {server}");

            UsingAnomalyAuth = true;

            if (!isSecondTime)
            {
                AuthStatusOverlay.BeginAuthenticating();
                if (!_connections.TryGetValue(server, out connection))
                {
                    var identity = PlayerAuth.GetIdentity();
                    ClientAuthSession clientEnv;
                    try
                    {
                        clientEnv = ClientAuthSession.Generate(identity);
                        clientEnv.BindIdentityRotationChain(PlayerAuth.GetIdentityRotationChain());
                        int serverPort = Convert.ToInt32(
                            Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.port);
                        clientEnv.BindConnectionContext(
                            Il2CppGameCore.Version.Major,
                            Il2CppGameCore.Version.Minor,
                            Il2CppGameCore.Version.Revision,
                            Il2CppGameCore.Version.BackwardCompatibility,
                            Il2CppGameCore.Version.BackwardCompatibility
                                ? Il2CppGameCore.Version.BackwardRevision
                                : (byte)0,
                            AuthConnectionContext.NormalizeEndpoint(server, serverPort));
                    }
                    finally
                    {
                        Array.Clear(identity, 0, identity.Length);
                    }
                    connection = new ServerConnection { Session = clientEnv };
                    _connections[server] = connection;
                }

                var clientHandshake = connection.Session.CreateHandshake(connection.RetryCookie);

                writer.PutBytesWithLength(clientHandshake.Encode());
                _phase2Sent = false;
            }
            else
            {
                if (connection.PasswordChallenge?.Required == true && connection.PasswordProof == null)
                    MelonLogger.Warning("Server requires a password but no password proof is available; phase-2 auth will be rejected.");

                var attempt = connection.Session.CreateLoginAttempt(
                    connection.Handshake,
                    connection.ServerIdentifier,
                    connection.PasswordChallenge,
                    PlayerAuth.Username,
                    connection.PasswordProof);
                writer.PutBytesWithLength(attempt.Encode());
                writer.Put(connection.ServerIdentifier, 0);
                writer.Put(PlayerAuth.Username, 0);

                using (var keys = connection.Session.DeriveSessionKeys(
                           connection.Handshake,
                           connection.ServerIdentifier,
                           connection.PasswordChallenge))
                {
                    AnomalyMessaging.ConfigureTransportSession(
                        keys.ClientToServerKey,
                        keys.ServerToClientKey,
                        keys.SessionId,
                        keys.TranscriptHash);
                }
                // Don't remove _connections[server] here — LiteNetLib calls
                // GetConnectData twice per connect and the duplicate must
                // still see the entry so it dedups instead of re-entering
                // phase-1. OnDisconnected clears the entry after phase-2.
                _phase2Sent = true;
            }
        }
        catch(Exception e)
        {
            MelonLogger.Error("Error during authentication to server: " + e);
        }
    }

    /// <returns>true to run the original handler, false to skip it</returns>
    internal static bool RejectAuth(DisconnectInfo info)
    {
        _authWriteCalled = false;
        MelonLogger.Warning($"Rejected Auth - Reason: {info.Reason}");
        var pos = info.AdditionalData._position;
        var data = info.AdditionalData;
        byte requestType;
        try
        {
            requestType = data.GetByte();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Server rejection did not contain a response code: {ex}");
            info.AdditionalData._position = pos;
            return true;
        }

        var server = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.clientAddress;
        var port = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.port.ToString();
        if (requestType == RetryCookieResponseCode)
        {
            try
            {
                var cookie = data.GetBytesWithLength();
                if (cookie == null || cookie.Length != ClientHandshake.RetryCookieSize)
                    throw new InvalidDataException($"Retry cookie length was {cookie?.Length ?? 0}, expected {ClientHandshake.RetryCookieSize}.");
                if (!_connections.TryGetValue(server, out var retryConnection)
                    || retryConnection.Session == null
                    || retryConnection.Handshake != null)
                    throw new InvalidOperationException("No matching phase-one client session exists.");

                retryConnection.RetryCookie = cookie;
                ConnectionInfoOverlay.BeginOverride(Tr.Get(TrKeys.Overlay_ConnectionAuthenticating));
                MelonCoroutines.Start(ConnectAgain(server, port));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to process protocol-v2 retry cookie: {ex}");
                CancelAuthentication(server);
            }
            return false;
        }

        if (requestType != HandshakeResponseCode)
        {
            AuthStatusOverlay.EndAuthenticating();
            ConnectionInfoOverlay.EndOverride();
            MelonLogger.Warning($"Server rejected with code {requestType}. Passing to base handler.");
            info.AdditionalData._position = pos;
            return true; // Not our rejection, let original handler process it
        }

        if (!_connections.TryGetValue(server, out var connection))
        {
            MelonLogger.Error("Received a server handshake without a matching client session.");
            return false;
        }

        try
        {
            var encodedHandshake = data.GetBytesWithLength();
            var handshake = ServerHandshake.Decode(encodedHandshake);
            var serverIdentifier = data.GetString();
            if (string.IsNullOrEmpty(serverIdentifier)
                || Encoding.UTF8.GetByteCount(serverIdentifier) > AuthTranscript.MaximumServerIdentifierBytes)
                throw new InvalidDataException("Server identifier is empty or too long.");
            var passwordChallenge = ReadPasswordChallenge(data);

            if (!connection.Session.ValidateServerHandshake(handshake, serverIdentifier, passwordChallenge))
                throw new CryptographicException("Server transcript signature is invalid.");

            connection.Handshake = handshake;
            connection.ServerIdentifier = serverIdentifier;
            connection.PasswordChallenge = passwordChallenge;
            connection.ServerFingerprint = handshake.GetIdentityFingerprint();
        }
        catch (AnomalyProtocolVersionMismatchException pv)
        {
            MelonLogger.Error($"Server speaks a different Anomaly protocol version. {pv.Message}");
            CancelAuthentication(server);
            return false;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Rejected unauthenticated or malformed server handshake: {ex}");
            CancelAuthentication(server);
            return false;
        }

        var trustEndpoint = BuildTrustEndpoint(server, port);
        var trustState = ServerTrustStore.Evaluate(
            trustEndpoint,
            connection.ServerFingerprint,
            out var expectedFingerprint);
        if (trustState == ServerTrustState.Changed)
        {
            if (ServerTrustStore.TryApplySignedRotation(
                    trustEndpoint,
                    connection.Handshake.identityPublic,
                    connection.Handshake.identityRotationRecord,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    out var rotationError))
            {
                MelonLogger.Msg(
                    $"Accepted an old-key-signed server identity rotation for {trustEndpoint}: "
                    + $"{expectedFingerprint} -> {connection.ServerFingerprint}.");
                ContinueAfterServerTrust(server, port, connection);
                return false;
            }

            MelonLogger.Error(
                $"SECURITY: server key changed for {trustEndpoint}. "
                + $"Expected {expectedFingerprint}, received {connection.ServerFingerprint}. "
                + $"Automatic rotation was rejected: {rotationError}. Connection blocked. "
                + "Verify the fingerprint out of band and use a future launcher/operator trust-reset workflow; "
                + "the in-game client will not replace a changed pin.");
            CancelAuthentication(server);
            return false;
        }
        if (trustState == ServerTrustState.Unavailable)
        {
            MelonLogger.Error("Server trust store is unavailable; connection blocked rather than silently forgetting pins.");
            CancelAuthentication(server);
            return false;
        }

        if (trustState == ServerTrustState.FirstSeen)
        {
            var formattedFingerprint = ServerTrustStore.FormatFingerprint(connection.ServerFingerprint);
            PasswordPrompt.ShowConfirmation(
                "Trust this Anomaly server key?",
                trustEndpoint + "\n\n" + formattedFingerprint,
                () =>
                {
                    if (!ServerTrustStore.TryPinFirstSeen(
                            trustEndpoint,
                            connection.ServerFingerprint,
                            connection.Handshake.identityPublic,
                            out var pinError))
                    {
                        MelonLogger.Error($"Failed to save server key pin: {pinError}");
                        CancelAuthentication(server);
                        return;
                    }
                    ContinueAfterServerTrust(server, port, connection);
                },
                () => CancelAuthentication(server));
            return false;
        }

        if (!ServerTrustStore.TryRememberTrustedIdentity(
                trustEndpoint,
                connection.ServerFingerprint,
                connection.Handshake.identityPublic,
                out var rememberError))
        {
            // A legacy fingerprint pin still authenticates this connection, but
            // failure to persist the full key means a future signed rotation
            // cannot be verified automatically.
            MelonLogger.Warning($"Could not upgrade the trusted server key record: {rememberError}");
        }

        ContinueAfterServerTrust(server, port, connection);
        return false;
    }

    private static void ContinueAfterServerTrust(string server, string port, ServerConnection connection)
    {
        if (connection.PasswordChallenge?.Required == true)
        {
            PromptForPassword(server, port, connection);
            return;
        }

        ConnectionInfoOverlay.BeginOverride(Tr.Get(TrKeys.Overlay_ConnectionAuthenticating));
        MelonCoroutines.Start(ConnectAgain(server, port));
    }

    private static PasswordChallenge ReadPasswordChallenge(NetDataReader data)
    {
        if (data.EndOfData)
            return PasswordChallenge.NotRequired();

        var required = data.GetBool();
        if (!required)
            return PasswordChallenge.NotRequired();

        var salt = data.GetBytesWithLength();
        var iterations = data.GetInt();
        var nonce = data.GetBytesWithLength();

        if (salt == null || salt.Length != PasswordAuth.SaltSize)
            throw new InvalidDataException($"Password salt length was {salt?.Length ?? 0}, expected {PasswordAuth.SaltSize}.");
        if (iterations < PasswordAuth.MinimumIterations || iterations > PasswordAuth.MaximumIterations)
            throw new InvalidDataException(
                $"Password KDF iterations {iterations} outside [{PasswordAuth.MinimumIterations}, {PasswordAuth.MaximumIterations}].");
        if (nonce == null || nonce.Length != PasswordAuth.NonceSize)
            throw new InvalidDataException($"Password nonce length was {nonce?.Length ?? 0}, expected {PasswordAuth.NonceSize}.");

        return new PasswordChallenge
        {
            Required = true,
            Salt = salt,
            Iterations = iterations,
            Nonce = nonce,
        };
    }

    private static void PromptForPassword(string server, string port, ServerConnection connection)
    {
        ConnectionInfoOverlay.BeginOverride(Tr.Get(TrKeys.Overlay_PasswordRequired));
        PasswordPrompt.Show(
            password =>
            {
                ConnectionInfoOverlay.BeginOverride(Tr.Get(TrKeys.Overlay_ConnectionAuthenticating));
                var generation = _connectionGeneration;
                var proofTask = Task.Run(() => connection.Session.CreatePasswordProof(
                        password,
                        connection.Handshake,
                        connection.ServerIdentifier,
                        connection.PasswordChallenge));
                MelonCoroutines.Start(CompletePasswordProof(server, port, connection, generation, proofTask));
            },
            () => CancelAuthentication(server));
    }

    private static IEnumerator CompletePasswordProof(
        string server,
        string port,
        ServerConnection connection,
        int generation,
        Task<byte[]> proofTask)
    {
        while (!proofTask.IsCompleted)
            yield return null;

        if (generation != _connectionGeneration)
            yield break;

        try
        {
            connection.PasswordProof = proofTask.GetAwaiter().GetResult();
            MelonCoroutines.Start(ConnectAgain(server, port));
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Failed to prepare bounded password proof: {ex}");
            CancelAuthentication(server);
        }
    }

    private static void CancelAuthentication(string server)
    {
        PasswordPrompt.Hide();
        _connections.Remove(server);
        ClearCurrentKeys();
        _phase2Sent = false;
        _connectionGeneration++;
        AuthStatusOverlay.EndAuthenticating();
        ConnectionInfoOverlay.EndOverride();
    }

    private static string BuildTrustEndpoint(string server, string port)
    {
        if (!int.TryParse(port, out int parsedPort))
            throw new InvalidDataException("Server port was not numeric.");
        return AuthConnectionContext.NormalizeEndpoint(server, parsedPort);
    }

    private static void ClearCurrentKeys()
    {
        AnomalyMessaging.ClearTransportSession();
    }

    private static IEnumerator ConnectAgain(string ip, string port)
    {
        int gen = _connectionGeneration;
        yield return new WaitForSeconds(2);
        // If a disconnect/reconnect happened during the wait, don't proceed
        // with stale phase 2 data — the server session is gone.
        if (gen != _connectionGeneration)
        {
            MelonLogger.Msg("ConnectAgain cancelled — connection generation changed (likely round restart)");
            yield break;
        }
        _authWriteCalled = false;
        Il2CppGameCore.Console.Singleton.TypeCommand("connect " + ip + ":" + port);
    }

    private static PlayerAuth CreateNew(string name = "")
    {
        MelonLogger.Msg("Create new");
        var auth = new PlayerAuth()
        {
            Username = (name ?? string.Empty).Trim(),
        };
        auth.SetIdentity(AuthCrypto.CreateIdentity());

        SavePlayerAuth(auth);

        return auth;
    }

    internal static bool TryRotateClientIdentity(out string error)
    {
        error = null;
        if (UsingAnomalyAuth || _connections.Count != 0)
        {
            error = "Disconnect from the server before rotating the client identity.";
            return false;
        }

        byte[] oldIdentity = null;
        byte[] newIdentity = null;
        try
        {
            oldIdentity = PlayerAuth.GetIdentity();
            newIdentity = AuthCrypto.CreateIdentity();
            var existingChain = PlayerAuth.GetIdentityRotationChain();
            var nextChain = existingChain == null
                ? ClientIdentityRotationChain.Create(oldIdentity, newIdentity)
                : existingChain.Append(oldIdentity, newIdentity);

            var updated = PlayerAuth;
            updated.SetIdentity(newIdentity);
            updated.SetIdentityRotationChain(nextChain);

            // Persist the new private key and its old-key authorization together
            // before changing the live identity. A partial rotation must never
            // strand the player with an unprovable enrollment change.
            SavePlayerAuth(updated);
            PlayerAuth = updated;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (oldIdentity != null)
                Array.Clear(oldIdentity, 0, oldIdentity.Length);
            if (newIdentity != null)
                Array.Clear(newIdentity, 0, newIdentity.Length);
        }
    }

    private static void ValidateStoredIdentityLineage(PlayerAuth auth)
    {
        var chain = auth.GetIdentityRotationChain();
        if (chain == null)
            return;

        byte[] identity = auth.GetIdentity();
        try
        {
            byte[] currentPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(identity));
            if (!chain.TryValidate(currentPublic, out _, out var error))
                throw new InvalidDataException("Stored client identity rotation chain is invalid: " + error);
        }
        finally
        {
            Array.Clear(identity, 0, identity.Length);
        }
    }

    private static void PromptForFirstRunNickname()
    {
        PasswordPrompt.ShowTextInput(
            "Choose your Anomaly nickname",
            "This name is sent to servers. Anomaly will not use your Windows username. Enter 1-32 visible characters.",
            nickname =>
            {
                nickname = (nickname ?? string.Empty).Trim();
                if (nickname.Length < 1 || nickname.Length > 32 || nickname.Any(char.IsControl))
                {
                    MelonLogger.Warning("Nickname must contain 1-32 visible characters.");
                    PromptForFirstRunNickname();
                    return;
                }

                var previousAuth = PlayerAuth;
                var auth = previousAuth;
                auth.Username = nickname;
                PlayerAuth = auth;
                try
                {
                    SavePlayerAuth(PlayerAuth);
                    MelonLogger.Msg("Anomaly nickname saved.");
                }
                catch (Exception ex)
                {
                    PlayerAuth = previousAuth;
                    MelonLogger.Error($"Failed to save nickname and identity: {ex}");
                    PromptForFirstRunNickname();
                }
            },
            () => MelonLogger.Warning("Nickname selection cancelled; Anomaly authentication remains disabled."));
    }

    private static void SavePlayerAuth(PlayerAuth auth)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AuthFilePath));
        var tempPath = AuthFilePath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonConvert.SerializeObject(auth));
            if (File.Exists(AuthFilePath))
                File.Replace(tempPath, AuthFilePath, null);
            else
                File.Move(tempPath, AuthFilePath);
        }
        catch
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
            throw;
        }
    }

    public static void OnDisconnected()
    {
        _authWriteCalled = false;
        UsingAnomalyAuth = false;
        AuthStatusOverlay.EndAuthenticating();

        // A disconnect BEFORE phase-2 was sent is the transient reject that
        // happens between phase-1 (handshake) and phase-2 (login) — the
        // server rejects the phase-1 packet on purpose to push the handshake
        // back in the rejection data. We need to preserve _connections and
        // _connectionGeneration so ConnectAgain can use the stored handshake.
        // The ConnectionInfoOverlay must ALSO stay active across the transient
        // — vanilla SCPSL's OnClientDisconnect runs right after this prefix
        // and writes its kick text into ReasonContent; the LateUpdate-based
        // override only wins if it's still active when that write happens.
        //
        // A disconnect AFTER phase-2 was sent ends the session (success or
        // failure); the cached handshake is single-use server-side, so drop
        // it and bump the generation to cancel any stale ConnectAgain.
        if (_phase2Sent)
        {
            ConnectionInfoOverlay.EndOverride();
            _connections.Clear();
            ClearCurrentKeys();
            _connectionGeneration++;
            _phase2Sent = false;
        }

        MelonLogger.Msg("OnDisconnected");
    }
}
