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
    // IPv6/UDP overhead (46), LiteNetLib connect header (18), and the largest
    // IPv6 SocketAddress (28) leave this many bytes for GetConnectData.
    private const int MaximumConnectDataBytes = 1432 - 46 - 18 - 28;

    private sealed class ClientModInventoryPreparationException : Exception
    {
        internal ClientModInventoryPreparationException(string message, Exception inner = null)
            : base(message, inner) { }
    }

    private static Dictionary<string, ServerConnection> _connections = new();
    private static readonly ConnectionAttemptGate AttemptGate = new();
    private static readonly TransportDisconnectGate TransportDisconnects = new();
    private static readonly AdmissionSendGate<ClientAdmissionSnapshot> AdmissionSends = new();
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
        _connections.Clear();
        AdmissionSends.BeginScope();
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
            ClientAdmissionGuard.EnsureAvailable();
            if (!Core.RequiredConnectionFeaturesReady)
            {
                MelonLogger.Error(
                    "Authentication is disabled because required Anomaly lifecycle or Harmony hooks are not healthy.");
                ShowAuthenticationFailure(Tr.Get(TrKeys.Auth_FeaturesUnavailable),
                    Tr.Get(TrKeys.Auth_FeaturesAction, "anomaly.status"), "AUTH-FEATURES");
                return;
            }
            if (!Anomaly.Client.Patches.Mirror.NetworkManagerRegisterClientMessages.IsReady)
            {
                MelonLogger.Error("Authentication is disabled because required Anomaly networking services are not ready.");
                ShowAuthenticationFailure(Tr.Get(TrKeys.Auth_NetworkNotReady),
                    Tr.Get(TrKeys.Auth_NetworkAction), "AUTH-NETWORK-INIT");
                return;
            }
            if (!Anomaly.Client.Patches.Mirror.EncryptedChannelHandlerFix.IsReady)
            {
                MelonLogger.Error(
                    "Authentication is disabled because the required encrypted-channel receive bridge is not ready: " +
                    (Anomaly.Client.Patches.Mirror.EncryptedChannelHandlerFix.LastError ?? "unknown failure"));
                ShowAuthenticationFailure(Tr.Get(TrKeys.Auth_ReceiverUnavailable),
                    Tr.Get(TrKeys.Auth_ReceiverAction, "anomaly.status"), "AUTH-RECEIVER");
                return;
            }
            if (InitializationState != AuthInitializationState.Ready)
            {
                MelonLogger.Error(
                    "Authentication is disabled because the client identity profile is not ready: "
                    + (InitializationError ?? InitializationState.ToString()));
                ShowAuthenticationFailure(Tr.Get(TrKeys.Auth_ProfileUnavailable),
                    Tr.Get(TrKeys.Auth_ProfileAction), "AUTH-PROFILE");
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
                if (!ValidateAuthenticationSend())
                    throw new ClientIntegrityException(AdmissionSends.Current.Failure);
                try { MelonLogger.Warning("AuthWrite called again for the same native writer — ignoring duplicate GetConnectData call"); }
                catch { }
                return;
            }
            TransportDisconnects.Begin(AttemptGate.Generation);

            // Every re-entry issued by Anomaly is explicitly scheduled and is
            // single-use. Anything else is a fresh user connection and must
            // revoke stale handshakes, prompt callbacks, catalog state, and keys.
            if (disposition == ConnectionWriterDisposition.NewUserAttempt)
                BeginUserConnectionScope();

            if (!ValidateAuthenticationSend())
                throw new ClientIntegrityException(AdmissionSends.Current.Failure);

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

            if (isSecondTime && connection.Admission == null)
            {
                try
                {
                    // Capture once for this signed login. Invalid or oversized
                    // metadata must stop the attempt instead of silently omitting mods.
                    var admission = ClientModInventoryCollector.Capture();
                    connection.Session.BindAdmission(admission.Inventory, admission.Audit);
                    connection.Admission = admission;
                    AdmissionSends.Current.Bind(admission);
                }
                catch (ClientIntegrityException) { throw; }
                catch (Exception ex)
                {
                    throw new ClientModInventoryPreparationException(
                        Tr.Get(TrKeys.Auth_ModInventoryInvalid), ex);
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
                EnsureConnectDataFits(writer);
                _phase2Sent = false;
                AdmissionSends.Current.PrepareWriter(writer.Pointer.ToInt64());
            }
            else
            {
                ClientAdmissionGuard.EnsureCurrent(connection.Admission, reconcile: true);
                if (connection.PasswordChallenge?.Required == true && connection.PasswordProof == null)
                    MelonLogger.Warning("Server requires a password but no password proof is available; phase-2 auth will be rejected.");

                var attempt = connection.Session.CreateLoginAttempt(
                    connection.Handshake,
                    connection.ServerIdentifier,
                    connection.PasswordChallenge,
                    PlayerAuth.Username,
                    connection.PasswordProof);
                var encodedAttempt = attempt.Encode();
                ClientAdmissionGuard.EnsureCurrent(connection.Admission);
                writer.PutBytesWithLength(encodedAttempt);
                writer.Put(connection.ServerIdentifier, 0);
                writer.Put(PlayerAuth.Username, 0);
                EnsureConnectDataFits(writer);
                ClientAdmissionGuard.EnsureCurrent(connection.Admission);

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
                // Retain this attempt until its real disconnect boundary. The writer
                // transaction gate protects reentry without resetting handshake state.
                AdmissionSends.Current.PrepareWriter(writer.Pointer.ToInt64());
                _phase2Sent = true;
            }
        }
        catch (ClientIntegrityException e)
        {
            try { writer?.Reset(); } catch { }
            FailIntegrity(e.Message);
        }
        catch (ClientModInventoryPreparationException e)
        {
            try { writer?.Reset(); } catch { }
            CancelAuthentication(connectionKey, clearAllConnections: true);
            AnomalyMessaging.FailClosedTransport("Connection data preparation failed: " + e.GetType().Name + ".");
            ShowAuthenticationFailure(Tr.Get(TrKeys.Auth_ModInventoryUnavailable),
                e.Message, "AUTH-MOD-INVENTORY");
            MelonLogger.Error("Client mod inventory preparation failed: " + e);
        }
        catch(Exception e)
        {
            try { writer?.Reset(); } catch { }
            CancelAuthentication(connectionKey, clearAllConnections: true);
            AnomalyMessaging.FailClosedTransport(
                "Connection authentication failed: " + e.GetType().Name + ".");
            ShowAuthenticationFailure(Tr.Get(TrKeys.Auth_PrepareFailed),
                Tr.Get(TrKeys.Auth_PrepareAction), "AUTH-PREPARE");
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

    // Called at the last managed transport boundaries, including retransmission.
    // This path may run off-thread: latch the failure and let OnUpdate perform UI
    // and transport teardown. Every later send is denied until that teardown.
    internal static bool ValidateAuthenticationSend()
    {
        var scope = AdmissionSends.Current;
        if (scope.Failure != null)
            return false;
        var admission = scope.Admission;
        if (admission == null)
            return AdmissionSends.IsCurrent(scope);
        try
        {
            ClientAdmissionGuard.EnsureCurrent(admission);
            return scope.Failure == null && AdmissionSends.IsCurrent(scope);
        }
        catch (Exception exception)
        {
            scope.Fail("Authentication integrity changed: " + exception.Message);
            return false;
        }
    }

    // The game calls GetConnectData before ConnectClient. Without a prepared
    // Anomaly handshake, a removed auth prefix must not send vanilla data.
    internal static bool ValidateConnectionStart(NetDataWriter writer)
    {
        var scope = AdmissionSends.Current;
        if (scope.Failure != null) return false;
        try { Core.EnsureOfflineAuthenticationMode(); }
        catch (ClientIntegrityException exception)
        {
            scope.Fail(exception.Message);
            return false;
        }
        if (writer == null || !scope.IsPreparedWriter(writer.Pointer.ToInt64(), Il2CppMirror.NetworkServer.active))
        {
            scope.Fail("Connection data was not prepared by Anomaly authentication.");
            return false;
        }
        return ValidateAuthenticationSend();
    }

    internal static void EnterLocalHostScope()
    {
        // Only the existing in-process host branch calls this. Remote loopback
        // connections still use the signed admission path and cannot opt out.
        if (!Il2CppMirror.NetworkServer.active)
            return;
        ResetForLocalHost();
    }

    internal static void PrepareForLocalHostStart()
    {
        // StartHost uses Mirror's in-process connection pair and may never invoke
        // GetConnectData. Clear the previous remote scope at the real host entry.
        if (ServerStatic.IsDedicated || Il2CppMirror.NetworkServer.active || Il2CppMirror.NetworkClient.active ||
            Il2CppMirror.NetworkManager.singleton?.mode != Il2CppMirror.NetworkManagerMode.Offline)
            throw new InvalidOperationException("A local host may only start from the inactive local client.");
        ResetForLocalHost();
    }

    private static void ResetForLocalHost()
    {
        CancelAuthentication(null, clearAllConnections: true);
        AdmissionSends.BeginScope();
        AdmissionSends.Current.PermitLocalHost();
    }

    internal static void UpdateIntegrityMonitoring()
    {
        var scope = AdmissionSends.Current;
        var admission = scope.Admission;
        if (admission != null)
        {
            try
            {
                // Bootstrap owns recurring reconciliation. Keep transport admission
                // health, mutation barriers, and generations checked every frame.
                ClientAdmissionGuard.EnsureCurrent(admission);
            }
            catch (Exception exception)
            {
                scope.Fail("Authentication integrity changed: " + exception.Message);
            }
        }
        HandleIntegrityFailure(scope);
    }

    private static void FailIntegrity(string reason)
    {
        var scope = AdmissionSends.Current;
        scope.Fail(reason);
        HandleIntegrityFailure(scope);
    }

    private static void HandleIntegrityFailure(AdmissionSendScope<ClientAdmissionSnapshot> scope)
    {
        // Connection transitions and teardown both run on the main thread.
        // A late worker can only leave a failure in the scope it captured.
        if (!AdmissionSends.TryBeginFailureHandling(scope, out string reason))
            return;
        CancelAuthentication(null, clearAllConnections: true);
        AnomalyMessaging.FailClosedTransport(reason);
        try { Il2CppMirror.NetworkManager.singleton?.StopClient(); }
        catch (Exception exception) { SafeLogError("Could not stop the invalidated transport: " + exception); }
        ShowAuthenticationFailure(Tr.Get(TrKeys.Auth_IntegrityFailed),
            Tr.Get(TrKeys.Auth_IntegrityAction), "AUTH-INTEGRITY");
        SafeLogError(reason);
    }

    private static void EnsureConnectDataFits(NetDataWriter writer)
    {
        if (writer.Length > MaximumConnectDataBytes)
            throw new ClientModInventoryPreparationException(
                Tr.Get(TrKeys.Auth_ModInventoryTooLarge, writer.Length, MaximumConnectDataBytes));
    }

    /// <returns>true to run the original handler, false to skip it</returns>
    internal static bool RejectAuth(DisconnectInfo info)
    {
        NetDataReader data = null;
        try
        {
            var original = info.AdditionalData;
            if (original != null)
            {
                var source = original.RawData;
                int position = original._position;
                int count = original.AvailableBytes;
                if (source == null || position < 0 || count < 0 || position > source.Length - count)
                    throw new InvalidDataException("Server rejection reader bounds were invalid.");
                var snapshot = new byte[count];
                for (int index = 0; index < count; index++)
                    snapshot[index] = source[position + index];
                data = new NetDataReader(snapshot);
            }
        }
        catch (Exception exception)
        {
            MelonLogger.Error("Could not capture the server rejection: " + exception);
            FailAuthentication(null, Tr.Get(TrKeys.Auth_ConnectionEnded, info.Reason),
                Tr.Get(TrKeys.Auth_NoResponseAction), "AUTH-NO-RESPONSE");
            return true;
        }
        // Parsing may prompt or schedule a reconnect. Keep the native refusal
        // reader untouched, including when the original game handler must run.
        return RejectAuth(info.Reason, data);
    }

    private static bool RejectAuth(DisconnectReason reason, NetDataReader data)
    {
        AttemptGate.CompleteWriterAttempt();
        var server = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.clientAddress;
        var port = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.port.ToString();
        var connectionKey = BuildTrustEndpoint(server, port);
        if (data == null)
        {
            MelonLogger.Warning($"Authentication connection ended without response data ({reason}).");
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_ConnectionEnded, reason),
                Tr.Get(TrKeys.Auth_NoResponseAction), "AUTH-NO-RESPONSE");
            return true;
        }
        byte requestType;
        try
        {
            requestType = data.GetByte();
        }
        catch (Exception ex)
        {
            CancelAuthentication(connectionKey);
            MelonLogger.Error($"Server rejection did not contain a response code: {ex}");
            ShowAuthenticationFailure(Tr.Get(TrKeys.Auth_ConnectionEnded, reason),
                Tr.Get(TrKeys.Auth_NoResponseAction), "AUTH-NO-RESPONSE");
            return true;
        }
        if (requestType == RetryCookieResponseCode || requestType == HandshakeResponseCode)
            MelonLogger.Msg($"Authentication continuation received ({requestType}, {reason}).");
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
                FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_RetryInvalid),
                    Tr.Get(TrKeys.Auth_RetryAction), "AUTH-RETRY");
            }
            return false;
        }

        if (requestType == (byte)RejectionReason.Custom)
        {
            MelonLogger.Warning($"Server rejected authentication with a custom response ({reason}).");
            HandleCustomRejection(connectionKey, data);
            return false;
        }

        if (requestType != HandshakeResponseCode)
        {
            CancelAuthentication(connectionKey);
            MelonLogger.Warning($"Server rejected with code {requestType}. Passing to base handler.");
            ShowAuthenticationFailure(Tr.Get(TrKeys.Auth_Rejected),
                Tr.Get(TrKeys.Auth_RejectedAction), "AUTH-REJECT-" + requestType);
            return true; // Not our rejection, let original handler process it
        }

        if (!_connections.TryGetValue(connectionKey, out var connection))
        {
            MelonLogger.Error("Received a server handshake without a matching client session.");
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_SessionMismatch),
                Tr.Get(TrKeys.Auth_SessionAction), "AUTH-SESSION");
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
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_VersionMismatch),
                Tr.Get(TrKeys.Auth_VersionAction), "AUTH-VERSION");
            return false;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Rejected unauthenticated or malformed server handshake: {ex}");
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_HandshakeInvalid),
                Tr.Get(TrKeys.Auth_HandshakeAction), "AUTH-HANDSHAKE");
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
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_KeyChanged),
                Tr.Get(TrKeys.Auth_KeyChangedAction), "AUTH-KEY-CHANGED");
            return false;
        }
        if (trustState == ServerTrustState.Unavailable)
        {
            MelonLogger.Error("Server trust store is unavailable; connection blocked rather than silently forgetting pins.");
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_TrustStoreUnavailable),
                Tr.Get(TrKeys.Auth_TrustStoreAction), "AUTH-TRUST-STORE");
            return false;
        }

        if (trustState == ServerTrustState.FirstSeen)
        {
            var formattedFingerprint = ServerTrustStore.FormatFingerprint(connection.ServerFingerprint);
            int trustGeneration = AttemptGate.Generation;
            PasswordPrompt.ShowConfirmation(
                PromptOwner,
                Tr.Get(TrKeys.Auth_TrustTitle),
                Tr.Get(TrKeys.Auth_TrustDetails, trustEndpoint, formattedFingerprint),
                Tr.Get(TrKeys.Prompt_TrustConnect),
                Tr.Get(TrKeys.Prompt_Cancel),
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
                        FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_TrustSaveFailed),
                            Tr.Get(TrKeys.Auth_TrustSaveAction), "AUTH-TRUST-SAVE");
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

    private static void HandleCustomRejection(string connectionKey, NetDataReader data)
    {
        string reason = null;
        try
        {
            // The custom rejection contains exactly one bounded LiteNetLib string.
            if (data.AvailableBytes < 1 ||
                data.AvailableBytes > ModAdmissionResponseCodec.MaxUtf8Bytes + 4)
                throw new InvalidDataException("Custom rejection length is outside the accepted range.");
            reason = data.GetString(ModAdmissionResponseCodec.MaxUtf8Bytes);
            if (string.IsNullOrWhiteSpace(reason) || !data.EndOfData ||
                Encoding.UTF8.GetByteCount(reason) > ModAdmissionResponseCodec.MaxUtf8Bytes)
                throw new InvalidDataException("Custom rejection has invalid or trailing data.");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("Could not read the server's custom rejection: " + ex.Message);
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_Rejected),
                Tr.Get(TrKeys.Auth_CustomRejectedAction), "AUTH-CUSTOM-REJECT");
            return;
        }

        if (ClientIntegrityResponseCodec.TryDecode(reason, out var integrity))
        {
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_IntegrityRejected),
                Tr.Get(TrKeys.Auth_IntegrityRejectedAction, integrity.Flags), "AUTH-INTEGRITY");
            return;
        }

        if (ModAdmissionResponseCodec.TryDecode(reason, out var admission))
        {
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_ModRequirements),
                FormatModAdmissionIssues(admission), "AUTH-MOD-REQUIREMENTS");
            return;
        }

        if (reason.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_Rejected),
                Tr.Get(TrKeys.Auth_ModResponseAction),
                "AUTH-MOD-RESPONSE");
            return;
        }

        FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_CustomReason, SanitizeDisplayText(reason)),
            Tr.Get(TrKeys.Auth_ReviewRequirements), "AUTH-CUSTOM-REJECT");
    }

    private static string FormatModAdmissionIssues(ModAdmissionResponse response)
    {
        var details = new StringBuilder();
        foreach (ModAdmissionIssue issue in response.Issues)
        {
            if (details.Length != 0) details.Append('\n');
            string id = SanitizeDisplayText(issue.Id);
            string required = SanitizeDisplayText(issue.Required);
            string present = SanitizeDisplayText(issue.Present);
            switch (issue.Code)
            {
                case "missing":
                    details.Append(Tr.Get(TrKeys.Auth_ModMissing, id, required));
                    break;
                case "wrong_version":
                    details.Append(Tr.Get(TrKeys.Auth_ModWrongVersion, id, required, present));
                    break;
                case "blocked":
                case "unexpected":
                    string message = Tr.Get(issue.Code == "blocked" ? TrKeys.Auth_ModBlocked : TrKeys.Auth_ModUnexpected, id);
                    details.Append(string.IsNullOrEmpty(present)
                        ? message : Tr.Get(TrKeys.Auth_ModPresent, message, present));
                    break;
            }
        }
        if (response.OmittedCount != 0)
            details.Append('\n').Append(Tr.Get(TrKeys.Auth_ModIssuesOmitted, response.OmittedCount));
        details.Append("\n\n").Append(Tr.Get(TrKeys.Auth_ModRequirementsAction));
        return details.ToString();
    }

    private static string SanitizeDisplayText(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var safe = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (char.IsControl(character) && character != '\n' && character != '\r' && character != '\t')
                safe.Append(' ');
            else safe.Append(character);
        }
        return safe.ToString();
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
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_PasswordFailed),
                Tr.Get(TrKeys.Auth_PasswordAction), "AUTH-PASSWORD");
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

    private static void FailAuthentication(string connectionKey, string reason, string action, string code)
    {
        CancelAuthentication(connectionKey);
        ShowAuthenticationFailure(reason, action, code);
    }

    private static void ShowAuthenticationFailure(string reason, string action, string code)
    {
        try { ConnectionInfoOverlay.ShowFailure(reason, action, code); }
        catch (Exception exception) { SafeLogError("Failed to show terminal authentication details: " + exception); }
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
        AdmissionSends.BeginScope();
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
        AdmissionSends.Current.ClearAdmission();
        AdmissionSends.Current.RevokePreparedWriter();
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
            FailAuthentication(connectionKey, Tr.Get(TrKeys.Auth_ReconnectFailed),
                Tr.Get(TrKeys.Auth_ReconnectAction), "AUTH-RECONNECT");
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
            error = Tr.Get(TrKeys.Auth_RotationProfileUnavailable, InitializationState);
            return false;
        }
        if (UsingAnomalyAuth || _connections.Count != 0)
        {
            error = Tr.Get(TrKeys.Auth_RotationDisconnect);
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
            error = Tr.Get(TrKeys.Auth_RotationError, ex.GetType().Name);
            SafeLogError("Client identity rotation failed: " + ex);
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
            Tr.Get(TrKeys.Prompt_NicknameTitle),
            Tr.Get(TrKeys.Prompt_NicknameDetails),
            PlayerAuth.Username,
            nickname =>
            {
                nickname = (nickname ?? string.Empty).Trim();
                if (nickname.Length < 1 || nickname.Length > 32 || nickname.Any(char.IsControl))
                {
                    return Tr.Get(TrKeys.Prompt_NicknameInvalid);
                }

                var auth = PlayerAuth;
                auth.Username = nickname;
                try
                {
                    _profileStore.Save(auth);
                    PlayerAuth = auth;
                    MelonLogger.Msg("Anomaly nickname saved.");
                    Anomaly.Client.Patches.Menu.MainMenuCustomizer.RefreshIdentity();
                    return null;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Failed to save nickname and identity: {ex}");
                    return Tr.Get(TrKeys.Prompt_SaveError);
                }
            },
            () => MelonLogger.Warning("Nickname selection cancelled; Anomaly authentication remains disabled."));
    }

    public static void OnDisconnected()
    {
        // LiteNetLib bypasses OnConncetionRefused for network failures and a
        // connected peer's RemoteConnectionClose. The stock client ignores the
        // latter's reason by checking DisconnectPeerCalled (the local event).
        // Its callback may originate off-thread, so only capture there and show
        // the terminal notice here, before ordinary main-thread teardown.
        try
        {
            var host = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore.Host;
            bool observed = TransportDisconnects.TryConsumeNotice(
                AttemptGate.Generation, host?.Pointer.ToInt64() ?? 0, out var notice);
            if (observed && UsingAnomalyAuth)
            {
                if (notice.Outcome == TransportDisconnectGate.Kind.ServerClosed)
                    FailAuthentication(null, Tr.Get(TrKeys.Auth_ServerClosed, SanitizeDisplayText(notice.Reason)),
                        Tr.Get(TrKeys.Auth_ServerClosedAction), "CONNECTION-SERVER-CLOSED");
                else
                    FailAuthentication(null, Tr.Get(TrKeys.Auth_NoResponse, notice.Reason),
                        Tr.Get(TrKeys.Auth_ServerAvailabilityAction), "AUTH-NO-RESPONSE");
            }
        }
        catch (Exception exception)
        {
            // Presentation/interop failures must never prevent authentication teardown.
            TransportDisconnects.TryConsume(AttemptGate.Generation, 0, out _);
            SafeLogError("Could not display transport disconnect details: " + exception);
        }
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

    internal static void ObserveTransportDisconnect(NetPeer peer, DisconnectInfo info)
    {
        int generation = AttemptGate.Generation;
        var host = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore.Host;
        // Native peer/manager identity rejects late events from an older transport,
        // including when a same-generation authenticated reconnect has begun.
        if (peer == null || host == null || peer.NetManager == null) return;
        long currentHost = host.Pointer.ToInt64(), peerHost = peer.NetManager.Pointer.ToInt64();
        if (currentHost != peerHost) return;
        var reason = info.Reason;
        if (reason == DisconnectReason.RemoteConnectionClose)
        {
            var reader = info.AdditionalData;
            if (reader == null) return;
            var source = reader.RawData;
            int position = reader._position, count = reader.AvailableBytes;
            if (source == null || position < 0 || count <= 0 || count > StockDisconnectReason.MaximumPayloadBytes ||
                position > source.Length - count) return;
            var snapshot = new byte[count];
            for (int index = 0; index < count; index++) snapshot[index] = source[position + index];
            TransportDisconnects.ObserveServerClose(generation, currentHost, peerHost, snapshot);
            return;
        }
        bool networkFailure = reason == DisconnectReason.ConnectionFailed
            || reason == DisconnectReason.Timeout
            || reason == DisconnectReason.HostUnreachable
            || reason == DisconnectReason.NetworkUnreachable;
        TransportDisconnects.Observe(generation, currentHost, peerHost,
            networkFailure, reason.ToString());
    }

    internal static void ObserveLocalTransportStop() => TransportDisconnects.Cancel(AttemptGate.Generation);
}
