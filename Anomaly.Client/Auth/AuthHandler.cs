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
    private static long _nextIntegrityReconciliation;
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
                ShowAuthenticationFailure("Required Anomaly features are unavailable.",
                    "Restart with a matching Anomaly release and check anomaly.status for the failed feature.", "AUTH-FEATURES");
                return;
            }
            if (!Anomaly.Client.Patches.Mirror.NetworkManagerRegisterClientMessages.IsReady)
            {
                MelonLogger.Error("Authentication is disabled because required Anomaly networking services are not ready.");
                ShowAuthenticationFailure("Anomaly networking is not ready.",
                    "Wait for initialization to finish, then reconnect. If this repeats, restart the game.", "AUTH-NETWORK-INIT");
                return;
            }
            if (!Anomaly.Client.Patches.Mirror.EncryptedChannelHandlerFix.IsReady)
            {
                MelonLogger.Error(
                    "Authentication is disabled because the required encrypted-channel receive bridge is not ready: " +
                    (Anomaly.Client.Patches.Mirror.EncryptedChannelHandlerFix.LastError ?? "unknown failure"));
                ShowAuthenticationFailure("The authenticated message receiver is unavailable.",
                    "Restart with a matching Anomaly release and check anomaly.status for the failed networking feature.", "AUTH-RECEIVER");
                return;
            }
            if (InitializationState != AuthInitializationState.Ready)
            {
                MelonLogger.Error(
                    "Authentication is disabled because the client identity profile is not ready: "
                    + (InitializationError ?? InitializationState.ToString()));
                ShowAuthenticationFailure("The local identity profile could not be opened.",
                    "Check the game log for the profile error, repair the reported file or permissions, then restart. Your existing profile was left unchanged.", "AUTH-PROFILE");
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
                        "The loaded client mod list is invalid or too large. Check the game log for the affected mod.", ex);
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
                // Don't remove _connections[connectionKey] here — LiteNetLib calls
                // GetConnectData twice per connect and the duplicate must
                // still see the entry so it dedups instead of re-entering
                // phase-1. OnDisconnected clears the entry after phase-2.
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
            ShowAuthenticationFailure("The client mod inventory could not be sent.",
                e.Message, "AUTH-MOD-INVENTORY");
            MelonLogger.Error("Client mod inventory preparation failed: " + e);
        }
        catch(Exception e)
        {
            try { writer?.Reset(); } catch { }
            CancelAuthentication(connectionKey, clearAllConnections: true);
            AnomalyMessaging.FailClosedTransport(
                "Connection authentication failed: " + e.GetType().Name + ".");
            ShowAuthenticationFailure("Authentication could not be prepared.",
                "Check the game log and use a matching client/server release before reconnecting.", "AUTH-PREPARE");
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
    }

    internal static void UpdateIntegrityMonitoring()
    {
        var scope = AdmissionSends.Current;
        var admission = scope.Admission;
        if (admission != null)
        {
            try
            {
                long now = Environment.TickCount64;
                bool reconcile = now >= _nextIntegrityReconciliation;
                if (reconcile) _nextIntegrityReconciliation = now + 1000;
                ClientAdmissionGuard.EnsureCurrent(admission, reconcile);
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
        ShowAuthenticationFailure("Authentication integrity requirements were not met.",
            reason + " A new connection is required; the previous handshake was discarded.", "AUTH-INTEGRITY");
        SafeLogError(reason);
    }

    private static void EnsureConnectDataFits(NetDataWriter writer)
    {
        if (writer.Length > MaximumConnectDataBytes)
            throw new ClientModInventoryPreparationException(
                $"The connection data is {writer.Length} bytes; the safe limit is {MaximumConnectDataBytes} bytes. " +
                "Check the game log for oversized client mod metadata or identity history.");
    }

    /// <returns>true to run the original handler, false to skip it</returns>
    internal static bool RejectAuth(DisconnectInfo info)
    {
        AttemptGate.CompleteWriterAttempt();
        MelonLogger.Warning($"Rejected Auth - Reason: {info.Reason}");
        var data = info.AdditionalData;
        var server = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.clientAddress;
        var port = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.port.ToString();
        var connectionKey = BuildTrustEndpoint(server, port);
        if (data == null)
        {
            FailAuthentication(connectionKey, "The server connection ended: " + info.Reason + ".",
                "Check that the server is available and uses a matching Anomaly release, then reconnect.", "AUTH-NO-RESPONSE");
            return true;
        }
        var pos = data._position;
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
            ShowAuthenticationFailure("The server connection ended: " + info.Reason + ".",
                "Check that the server is available and uses a matching Anomaly release, then reconnect.", "AUTH-NO-RESPONSE");
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
                FailAuthentication(connectionKey, "The server retry response was invalid.",
                    "Reconnect once. If this repeats, ask the server operator to check its Anomaly release and logs.", "AUTH-RETRY");
            }
            return false;
        }

        if (requestType == (byte)RejectionReason.Custom)
        {
            HandleCustomRejection(connectionKey, data);
            return false;
        }

        if (requestType != HandshakeResponseCode)
        {
            info.AdditionalData._position = pos;
            CancelAuthentication(connectionKey);
            MelonLogger.Warning($"Server rejected with code {requestType}. Passing to base handler.");
            ShowAuthenticationFailure("The server rejected the connection.",
                "Check the server's connection requirements and password, then reconnect. The game log contains the server rejection.", "AUTH-REJECT-" + requestType);
            return true; // Not our rejection, let original handler process it
        }

        if (!_connections.TryGetValue(connectionKey, out var connection))
        {
            MelonLogger.Error("Received a server handshake without a matching client session.");
            FailAuthentication(connectionKey, "The server reply did not match this connection attempt.",
                "Reconnect to start a fresh authentication attempt.", "AUTH-SESSION");
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
            FailAuthentication(connectionKey, "The client and server use different Anomaly protocol versions.",
                "Install the matching Anomaly release requested by the server operator.", "AUTH-VERSION");
            return false;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Rejected unauthenticated or malformed server handshake: {ex}");
            FailAuthentication(connectionKey, "The server identity response could not be verified.",
                "Ask the server operator to check its release and identity configuration before retrying.", "AUTH-HANDSHAKE");
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
            FailAuthentication(connectionKey, "The remembered server identity has changed.",
                "Verify the new fingerprint with the operator using another trusted channel. Anomaly has kept the existing trusted key and blocked this connection.", "AUTH-KEY-CHANGED");
            return false;
        }
        if (trustState == ServerTrustState.Unavailable)
        {
            MelonLogger.Error("Server trust store is unavailable; connection blocked rather than silently forgetting pins.");
            FailAuthentication(connectionKey, "The local server trust store could not be opened.",
                "Check the game log for the file error and repair it before reconnecting. Existing trusted keys have not been replaced.", "AUTH-TRUST-STORE");
            return false;
        }

        if (trustState == ServerTrustState.FirstSeen)
        {
            var formattedFingerprint = ServerTrustStore.FormatFingerprint(connection.ServerFingerprint);
            int trustGeneration = AttemptGate.Generation;
            PasswordPrompt.ShowConfirmation(
                PromptOwner,
                "Trust this Anomaly server key?",
                "Server: " + trustEndpoint + "\n\nIdentity fingerprint:\n" + formattedFingerprint
                    + "\n\nCheck this fingerprint with the server operator before trusting it. This key will be remembered for future connections.",
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
                        FailAuthentication(connectionKey, "The server trust decision could not be saved.",
                            "Check that Anomaly's UserData folder is writable, then reconnect and review the fingerprint again.", "AUTH-TRUST-SAVE");
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
            FailAuthentication(connectionKey, "The server rejected the connection.",
                "Ask the server operator for its connection requirements.", "AUTH-CUSTOM-REJECT");
            return;
        }

        if (ClientIntegrityResponseCodec.TryDecode(reason, out var integrity))
        {
            FailAuthentication(connectionKey, "The server does not accept the reported authentication modifications.",
                "Reported audit: " + integrity.Flags + ". Remove the affected modifications and restart the game, " +
                "or ask the server owner whether they accept clients with this risk.", "AUTH-INTEGRITY");
            return;
        }

        if (ModAdmissionResponseCodec.TryDecode(reason, out var admission))
        {
            FailAuthentication(connectionKey, "The server's client mod requirements were not met.",
                FormatModAdmissionIssues(admission), "AUTH-MOD-REQUIREMENTS");
            return;
        }

        if (reason.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            FailAuthentication(connectionKey, "The server rejected the connection.",
                "The mod requirements could not be read. Use a matching Anomaly release or ask the server operator for details.",
                "AUTH-MOD-RESPONSE");
            return;
        }

        FailAuthentication(connectionKey, SanitizeDisplayText(reason),
            "Review the server's connection requirements before reconnecting.", "AUTH-CUSTOM-REJECT");
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
                    details.Append("Missing client mod ‘").Append(id).Append("’ (required: ")
                        .Append(required).Append(')');
                    break;
                case "wrong_version":
                    details.Append("Incorrect mod version: ‘").Append(id).Append("’ — required ")
                        .Append(required).Append(", present ").Append(present);
                    break;
                case "blocked":
                    details.Append("Blocked client mod: ‘").Append(id).Append('’');
                    if (!string.IsNullOrEmpty(present)) details.Append(" (present: ").Append(present).Append(')');
                    break;
                case "unexpected":
                    details.Append("Additional client mod not allowed: ‘").Append(id).Append('’');
                    if (!string.IsNullOrEmpty(present)) details.Append(" (present: ").Append(present).Append(')');
                    break;
            }
        }
        if (response.OmittedCount != 0)
            details.Append("\n").Append(response.OmittedCount).Append(" more mod issue(s) were omitted.");
        details.Append("\n\nUpdate your client mods to meet the server's requirements, then reconnect.");
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
            FailAuthentication(connectionKey, "The password response could not be prepared.",
                "Reconnect and try again. If this repeats, check the game log for the password proof error.", "AUTH-PASSWORD");
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
        AdmissionSends.Current.ClearAdmission();
        _nextIntegrityReconciliation = 0;
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
            FailAuthentication(connectionKey, "The authenticated reconnect could not be started.",
                "Return to the server browser and connect again.", "AUTH-RECONNECT");
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
        // LiteNetLib bypasses OnConncetionRefused for timeout/unreachable errors.
        // Its callback may originate off-thread, so only capture there and show
        // the terminal notice here, before ordinary main-thread teardown.
        try
        {
            var host = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore.Host;
            bool transportFailed = TransportDisconnects.TryConsume(
                AttemptGate.Generation, host?.Pointer.ToInt64() ?? 0, out string transportReason);
            if (transportFailed && UsingAnomalyAuth)
            {
                FailAuthentication(null, "The server did not respond (" + transportReason + ").",
                    "Check the server address and that the server is running, then reconnect. If it is reachable, check that client and server use matching Anomaly releases.",
                    "AUTH-NO-RESPONSE");
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

    internal static void ObserveTransportDisconnect(NetPeer peer, DisconnectReason reason)
    {
        int generation = AttemptGate.Generation;
        var host = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore.Host;
        // Native peer/manager identity rejects late events from an older transport,
        // including when a same-generation authenticated reconnect has begun.
        if (peer == null || host == null || peer.NetManager == null) return;
        bool networkFailure = reason == DisconnectReason.ConnectionFailed
            || reason == DisconnectReason.Timeout
            || reason == DisconnectReason.HostUnreachable
            || reason == DisconnectReason.NetworkUnreachable;
        TransportDisconnects.Observe(generation, host.Pointer.ToInt64(), peer.NetManager.Pointer.ToInt64(),
            networkFailure, reason.ToString());
    }

    internal static void ObserveLocalTransportStop() => TransportDisconnects.Cancel(AttemptGate.Generation);
}
