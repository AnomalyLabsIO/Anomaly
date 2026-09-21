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
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Components;
using Anomaly.Client.Patches;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Il2CppMEC;
using UnityEngine;
using Action = Il2CppSystem.Action;

namespace Anomaly.Client.Auth;

internal enum AuthInitializationState
{
    Uninitialized,
    Ready,
    Failed,
}

public static class AuthHandler
{
    private const string PromptOwner = "authentication";
    private const byte HandshakeResponseCode = 100;
    private const byte RetryCookieResponseCode = 101;
    private static Dictionary<string, ServerConnection> _connections = new();
    private static readonly ConnectionAttemptGate AttemptGate = new();
    private static PlayerAuthProfileStore _profileStore;
    
    public static bool UsingAnomalyAuth { get; internal set; }

    public static string AuthFilePath { get; private set; }
    public static PlayerAuth PlayerAuth { get; private set; }
    internal static AuthInitializationState InitializationState { get; private set; }
    internal static string InitializationError { get; private set; }
    internal static string CurrentServerFingerprint { get; private set; }

    internal static void Init()
    {
        AttemptGate.Invalidate();
        PatchInvocationGuard.BeginAuthenticationScope();
        _connections.Clear();
        _phase2Sent = false;
        UsingAnomalyAuth = false;
        InitializationState = AuthInitializationState.Uninitialized;
        InitializationError = null;
        PlayerAuth = default;
        CurrentServerFingerprint = null;
        ClearCurrentKeys();
        try { PasswordPrompt.HideAll(); }
        catch (Exception exception)
        {
            MelonLogger.Error("Failed to clear stale authentication prompts during initialization: " + exception);
        }
        AuthFilePath = Path.Combine(Paths.AnomalyPath, "user.json");
        _profileStore = new PlayerAuthProfileStore(AuthFilePath);

        try
        {
            ServerTrustStore.Load();
            bool existingProfile = _profileStore.Exists;
            bool migrated = false;
            var auth = existingProfile
                ? _profileStore.LoadExisting(out migrated)
                : CreateAndSaveNewProfile();

            PlayerAuth = auth;
            InitializationState = AuthInitializationState.Ready;
            MelonLogger.Msg(
                $"Auth initialized. Nickname selected: {!string.IsNullOrWhiteSpace(auth.Username)}, " +
                $"protected identity present: {!string.IsNullOrWhiteSpace(auth.Identity)}.");
            if (existingProfile && migrated)
                MelonLogger.Msg("Migrated the client identity profile to protected storage with a public identity field.");
            if (string.IsNullOrWhiteSpace(auth.Username))
                PromptForFirstRunNickname();
        }
        catch (Exception ex)
        {
            PlayerAuth = default;
            InitializationState = AuthInitializationState.Failed;
            InitializationError = ex.GetType().Name + ": " + ex.Message;
            MelonLogger.Error("Failed to initialize auth: " + ex);
            throw new InvalidOperationException(
                "Client identity initialization failed. The existing profile was left unchanged.", ex);
        }
    }

    private static bool _phase2Sent;

    internal static void AuthWrite(NetDataWriter writer)
    {
        string connectionKey = null;
        try
        {
            if (!Core.RequiredConnectionFeaturesReady)
            {
                MelonLogger.Error(
                    "Authentication is disabled because required Anomaly lifecycle or Harmony hooks are not healthy.");
                return;
            }
            if (!Anomaly.Client.Patches.Mirror.NetworkManagerRegisterClientMessages.IsReady)
            {
                MelonLogger.Error("Authentication is disabled because required Anomaly networking services are not ready.");
                return;
            }
            if (!Anomaly.Client.Patches.Mirror.EncryptedChannelHandlerFix.IsReady)
            {
                MelonLogger.Error(
                    "Authentication is disabled because the required encrypted-channel receive bridge is not ready: " +
                    (Anomaly.Client.Patches.Mirror.EncryptedChannelHandlerFix.LastError ?? "unknown failure"));
                return;
            }
            if (InitializationState != AuthInitializationState.Ready)
            {
                MelonLogger.Error(
                    "Authentication is disabled because the client identity profile is not ready: "
                    + (InitializationError ?? InitializationState.ToString()));
                return;
            }

            var transport = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton;
            var server = transport.clientAddress;
            var port = transport.port.ToString();
            connectionKey = BuildTrustEndpoint(server, port);

            // Bind deduplication to the concrete native writer and normalized
            // endpoint. A distinct writer is a new attempt even if a stale
            // disconnect callback has not yet cleared the previous writer.
            if (writer == null)
                throw new InvalidOperationException("LiteNetLib did not provide an authentication writer.");
            var disposition = AttemptGate.BeginWriterAttempt(
                connectionKey,
                writer.Pointer.ToInt64());
            if (disposition == ConnectionWriterDisposition.Duplicate)
            {
                try { MelonLogger.Warning("AuthWrite called again for the same native writer — ignoring duplicate GetConnectData call"); }
                catch { }
                return;
            }

            // Every re-entry issued by Anomaly is explicitly scheduled and is
            // single-use. Anything else is a fresh user connection and must
            // revoke stale handshakes, prompt callbacks, catalog state, and keys.
            if (disposition == ConnectionWriterDisposition.NewUserAttempt)
                BeginUserConnectionScope();

            if (string.IsNullOrWhiteSpace(PlayerAuth.Username))
            {
                PromptForFirstRunNickname();
                MelonLogger.Warning("Authentication is paused until a nickname is selected.");
                return;
            }

            var isSecondTime = false;

            if (_connections.TryGetValue(connectionKey, out var connection))
            {
                if (connection.Handshake != null)
                {
                    isSecondTime = true;
                }
                else if (connection.RetryCookie == null)
                {
                    _connections.Remove(connectionKey);
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

            MelonLogger.Msg($"AuthWrite phase: {(isSecondTime ? "2 (login)" : "1 (handshake)")}, server: {connectionKey}");

            UsingAnomalyAuth = true;

            if (!isSecondTime)
            {
                AuthStatusOverlay.BeginAuthenticating();
                if (!_connections.TryGetValue(connectionKey, out connection))
                {
                    var identity = PlayerAuth.GetIdentity();
                    ClientAuthSession clientEnv;
                    try
                    {
                        clientEnv = ClientAuthSession.Generate(identity);
                        clientEnv.BindIdentityRotationChain(PlayerAuth.GetIdentityRotationChain());
                        clientEnv.BindConnectionContext(
                            Il2CppGameCore.Version.Major,
                            Il2CppGameCore.Version.Minor,
                            Il2CppGameCore.Version.Revision,
                            Il2CppGameCore.Version.BackwardCompatibility,
                            Il2CppGameCore.Version.BackwardCompatibility
                                ? Il2CppGameCore.Version.BackwardRevision
                                : (byte)0,
                            connectionKey);
                    }
                    finally
                    {
                        Array.Clear(identity, 0, identity.Length);
                    }
                    connection = new ServerConnection { Session = clientEnv };
                    _connections[connectionKey] = connection;
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
                // Don't remove _connections[connectionKey] here — LiteNetLib calls
                // GetConnectData twice per connect and the duplicate must
                // still see the entry so it dedups instead of re-entering
                // phase-1. OnDisconnected clears the entry after phase-2.
                _phase2Sent = true;
            }
        }
        catch(Exception e)
        {
            CancelAuthentication(connectionKey, clearAllConnections: true);
            AnomalyMessaging.FailClosedTransport(
                "Connection authentication failed: " + e.GetType().Name + ".");
            try
            {
                FeatureHealth.Report(
                    "anomaly",
                    "authentication",
                    FeatureHealthStatus.Failed,
                    "Connection authentication failed: " + e.GetType().Name);
                MelonLogger.Error("Error during authentication to server: " + e);
            }
            catch { }
        }
    }

    /// <returns>true to run the original handler, false to skip it</returns>
    internal static bool RejectAuth(DisconnectInfo info)
    {
        AttemptGate.CompleteWriterAttempt();
        MelonLogger.Warning($"Rejected Auth - Reason: {info.Reason}");
        var pos = info.AdditionalData._position;
        var data = info.AdditionalData;
        var server = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.clientAddress;
        var port = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.port.ToString();
        var connectionKey = BuildTrustEndpoint(server, port);
        byte requestType;
        try
        {
            requestType = data.GetByte();
        }
        catch (Exception ex)
        {
            info.AdditionalData._position = pos;
            CancelAuthentication(connectionKey);
            MelonLogger.Error($"Server rejection did not contain a response code: {ex}");
            return true;
        }
        if (requestType == RetryCookieResponseCode)
        {
            try
            {
                var cookie = data.GetBytesWithLength();
                if (cookie == null || cookie.Length != ClientHandshake.RetryCookieSize)
                    throw new InvalidDataException($"Retry cookie length was {cookie?.Length ?? 0}, expected {ClientHandshake.RetryCookieSize}.");
                if (!_connections.TryGetValue(connectionKey, out var retryConnection)
                    || retryConnection.Session == null
                    || retryConnection.Handshake != null)
                    throw new InvalidOperationException("No matching phase-one client session exists.");

                retryConnection.RetryCookie = cookie;
                ConnectionInfoOverlay.BeginOverride(Tr.Get(TrKeys.Overlay_ConnectionAuthenticating));
                MelonCoroutines.Start(ConnectAgain(server, port, connectionKey));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to process the Anomaly retry cookie: {ex}");
                CancelAuthentication(connectionKey);
            }
            return false;
        }

        if (requestType != HandshakeResponseCode)
        {
            info.AdditionalData._position = pos;
            CancelAuthentication(connectionKey);
            MelonLogger.Warning($"Server rejected with code {requestType}. Passing to base handler.");
            return true; // Not our rejection, let original handler process it
        }

        if (!_connections.TryGetValue(connectionKey, out var connection))
        {
            MelonLogger.Error("Received a server handshake without a matching client session.");
            CancelAuthentication(connectionKey);
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
            CancelAuthentication(connectionKey);
            return false;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Rejected unauthenticated or malformed server handshake: {ex}");
            CancelAuthentication(connectionKey);
            return false;
        }

        var trustEndpoint = connectionKey;
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
                ContinueAfterServerTrust(connectionKey, server, port, connection);
                return false;
            }

            MelonLogger.Error(
                $"SECURITY: server key changed for {trustEndpoint}. "
                + $"Expected {expectedFingerprint}, received {connection.ServerFingerprint}. "
                + $"Automatic rotation was rejected: {rotationError}. Connection blocked. "
                + "Verify the fingerprint out of band and use a future launcher/operator trust-reset workflow; "
                + "the in-game client will not replace a changed pin.");
            CancelAuthentication(connectionKey);
            return false;
        }
        if (trustState == ServerTrustState.Unavailable)
        {
            MelonLogger.Error("Server trust store is unavailable; connection blocked rather than silently forgetting pins.");
            CancelAuthentication(connectionKey);
            return false;
        }

        if (trustState == ServerTrustState.FirstSeen)
        {
            var formattedFingerprint = ServerTrustStore.FormatFingerprint(connection.ServerFingerprint);
            int trustGeneration = AttemptGate.Generation;
            PasswordPrompt.ShowConfirmation(
                PromptOwner,
                "Trust this Anomaly server key?",
                trustEndpoint + "\n\n" + formattedFingerprint,
                () =>
                {
                    if (!IsCurrentConnection(connectionKey, connection, trustGeneration))
                        return;
                    if (!ServerTrustStore.TryPinFirstSeen(
                            trustEndpoint,
                            connection.ServerFingerprint,
                            connection.Handshake.identityPublic,
                            out var pinError))
                    {
                        MelonLogger.Error($"Failed to save server key pin: {pinError}");
                        CancelAuthentication(connectionKey);
                        return;
                    }
                    ContinueAfterServerTrust(connectionKey, server, port, connection);
                },
                () =>
                {
                    if (IsCurrentConnection(connectionKey, connection, trustGeneration))
                        CancelAuthentication(connectionKey);
                });
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

        ContinueAfterServerTrust(connectionKey, server, port, connection);
        return false;
    }

    private static void ContinueAfterServerTrust(
        string connectionKey,
        string server,
        string port,
        ServerConnection connection)
    {
        CurrentServerFingerprint = connection.ServerFingerprint;
        if (connection.PasswordChallenge?.Required == true)
        {
            PromptForPassword(connectionKey, server, port, connection);
            return;
        }

        ConnectionInfoOverlay.BeginOverride(Tr.Get(TrKeys.Overlay_ConnectionAuthenticating));
        MelonCoroutines.Start(ConnectAgain(server, port, connectionKey));
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

    private static void PromptForPassword(
        string connectionKey,
        string server,
        string port,
        ServerConnection connection)
    {
        ConnectionInfoOverlay.BeginOverride(Tr.Get(TrKeys.Overlay_PasswordRequired));
        int promptGeneration = AttemptGate.Generation;
        PasswordPrompt.Show(
            PromptOwner,
            password =>
            {
                if (!IsCurrentConnection(connectionKey, connection, promptGeneration))
                    return;
                ConnectionInfoOverlay.BeginOverride(Tr.Get(TrKeys.Overlay_ConnectionAuthenticating));
                var proofTask = Task.Run(() => connection.Session.CreatePasswordProof(
                        password,
                        connection.Handshake,
                        connection.ServerIdentifier,
                        connection.PasswordChallenge));
                MelonCoroutines.Start(CompletePasswordProof(
                    connectionKey,
                    server,
                    port,
                    connection,
                    promptGeneration,
                    proofTask));
            },
            () =>
            {
                if (IsCurrentConnection(connectionKey, connection, promptGeneration))
                    CancelAuthentication(connectionKey);
            });
    }

    private static IEnumerator CompletePasswordProof(
        string connectionKey,
        string server,
        string port,
        ServerConnection connection,
        int generation,
        Task<byte[]> proofTask)
    {
        while (!proofTask.IsCompleted)
            yield return null;

        if (!AttemptGate.IsCurrent(generation))
            yield break;

        try
        {
            connection.PasswordProof = proofTask.GetAwaiter().GetResult();
            MelonCoroutines.Start(ConnectAgain(server, port, connectionKey));
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Failed to prepare bounded password proof: {ex}");
            CancelAuthentication(connectionKey);
        }
    }

    private static void CancelAuthentication(string connectionKey, bool clearAllConnections = false)
    {
        try { AttemptGate.Invalidate(); }
        catch { AttemptGate.CompleteWriterAttempt(); }
        if (clearAllConnections || string.IsNullOrEmpty(connectionKey))
            _connections.Clear();
        else
            _connections.Remove(connectionKey);
        CurrentServerFingerprint = null;
        UsingAnomalyAuth = false;
        _phase2Sent = false;
        try { ClearCurrentKeys(); }
        catch (Exception exception)
        {
            SafeLogError("Failed to clear authentication keys during cancellation: " + exception);
        }
        try { PasswordPrompt.Hide(PromptOwner); }
        catch (Exception exception)
        {
            SafeLogError("Failed to hide the authentication prompt during cancellation: " + exception);
        }
        try { AuthStatusOverlay.EndAuthenticating(); }
        catch (Exception exception)
        {
            SafeLogError("Failed to clear authentication status during cancellation: " + exception);
        }
        try { ConnectionInfoOverlay.EndOverride(); }
        catch (Exception exception)
        {
            SafeLogError("Failed to clear connection details during cancellation: " + exception);
        }
    }

    private static void SafeLogError(string message)
    {
        try { MelonLogger.Error(message); }
        catch { }
    }

    private static bool IsCurrentConnection(
        string connectionKey,
        ServerConnection connection,
        int generation) =>
        AttemptGate.IsCurrent(generation) &&
        _connections.TryGetValue(connectionKey, out ServerConnection current) &&
        ReferenceEquals(current, connection);

    private static void BeginUserConnectionScope()
    {
        PatchInvocationGuard.BeginAuthenticationScope();
        _connections.Clear();
        CurrentServerFingerprint = null;
        UsingAnomalyAuth = false;
        _phase2Sent = false;
        ClearCurrentKeys();

        try { Anomaly.Client.Content.ContentCatalogClient.Reset(); }
        catch (Exception exception)
        {
            SafeLogError("Failed to clear stale content state before a new connection: " + exception);
        }
        try { PasswordPrompt.HideAll(); }
        catch (Exception exception)
        {
            SafeLogError("Failed to hide stale connection prompts before a new connection: " + exception);
        }
        try { AuthStatusOverlay.EndAuthenticating(); }
        catch (Exception exception)
        {
            SafeLogError("Failed to clear stale authentication status before a new connection: " + exception);
        }
        try { ConnectionInfoOverlay.EndOverride(); }
        catch (Exception exception)
        {
            SafeLogError("Failed to clear stale connection details before a new connection: " + exception);
        }
    }

    private static string BuildTrustEndpoint(string server, string port)
    {
        if (!int.TryParse(port, out int parsedPort))
            throw new InvalidDataException("Server port was not numeric.");
        return AuthConnectionContext.NormalizeEndpoint(server, parsedPort);
    }

    private static void ClearCurrentKeys()
    {
        CurrentServerFingerprint = null;
        AnomalyMessaging.ClearTransportSession();
    }

    private static IEnumerator ConnectAgain(string ip, string port, string connectionKey)
    {
        int gen = AttemptGate.Generation;
        yield return new WaitForSeconds(2);
        // If a disconnect/reconnect happened during the wait, don't proceed
        // with stale phase 2 data — the server session is gone.
        if (!AttemptGate.IsCurrent(gen))
        {
            MelonLogger.Msg("ConnectAgain cancelled — connection generation changed (likely round restart)");
            yield break;
        }
        if (!AttemptGate.TryScheduleReconnect(connectionKey, gen))
            yield break;
        AttemptGate.CompleteWriterAttempt();
        try
        {
            Il2CppGameCore.Console.Singleton.TypeCommand("connect " + ip + ":" + port);
        }
        catch (Exception exception)
        {
            MelonLogger.Error("Failed to initiate the authenticated reconnect: " + exception);
            CancelAuthentication(connectionKey);
        }
    }

    private static PlayerAuth CreateNewProfile(string name = "")
    {
        MelonLogger.Msg("Creating a new client identity profile.");
        var auth = new PlayerAuth()
        {
            Username = (name ?? string.Empty).Trim(),
        };
        var identity = AuthCrypto.CreateIdentity();
        try { auth.SetIdentity(identity); }
        finally { Array.Clear(identity, 0, identity.Length); }

        return auth;
    }

    private static PlayerAuth CreateAndSaveNewProfile(string name = "")
    {
        var auth = CreateNewProfile(name);
        _profileStore.Save(auth);
        return auth;
    }

    internal static bool TryRotateClientIdentity(out string error)
    {
        error = null;
        if (InitializationState != AuthInitializationState.Ready)
        {
            error = "The client identity profile is not ready: "
                + (InitializationError ?? InitializationState.ToString());
            return false;
        }
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
            _profileStore.Save(updated);
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

    private static void PromptForFirstRunNickname()
    {
        if (InitializationState != AuthInitializationState.Ready)
        {
            MelonLogger.Error("Nickname selection is unavailable because the client identity profile failed to initialize.");
            return;
        }

        PasswordPrompt.ShowTextInput(
            PromptOwner,
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
                    _profileStore.Save(PlayerAuth);
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

    public static void OnDisconnected()
    {
        AttemptGate.CompleteWriterAttempt();
        UsingAnomalyAuth = false;

        // A disconnect BEFORE phase-2 was sent is the transient reject that
        // happens between phase-1 (handshake) and phase-2 (login) — the
        // server rejects the phase-1 packet on purpose to push the handshake
        // back in the rejection data. We need to preserve _connections and
        // connection-attempt generation so ConnectAgain can use the stored handshake.
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
            _connections.Clear();
            CurrentServerFingerprint = null;
            ClearCurrentKeys();
            AttemptGate.Invalidate();
            _phase2Sent = false;
            PasswordPrompt.Hide(PromptOwner);
            try { ConnectionInfoOverlay.EndOverride(); }
            catch (Exception exception)
            {
                MelonLogger.Error("Failed to clear the terminal connection overlay: " + exception);
            }
        }

        try { AuthStatusOverlay.EndAuthenticating(); }
        catch (Exception exception)
        {
            MelonLogger.Error("Failed to clear the authentication overlay after logical teardown: " + exception);
        }

        MelonLogger.Msg("OnDisconnected");
    }
}
