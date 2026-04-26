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
    private static Dictionary<string, ServerConnection> _connections = new();
    
    public static byte[] CurrentKey { get; private set; }
    public static bool UsingAnomalyAuth { get; internal set; }

    public static string AuthFilePath { get; private set; }
    public static PlayerAuth PlayerAuth { get; private set; }

    internal static void Init()
    {
        try
        {
            AuthFilePath = Path.Combine(Paths.AnomalyPath, "user.json");
            if (!File.Exists(AuthFilePath))
                CreateNew();

            var auth = File.Exists(AuthFilePath) ? JsonConvert.DeserializeObject<PlayerAuth>(File.ReadAllText(AuthFilePath)) : CreateNew();

            if (string.IsNullOrEmpty(auth.Identity) || auth.Identity == "pending")
            {
                auth = CreateNew(auth.Username);
            }


            PlayerAuth = auth;
            MelonLogger.Msg($"Auth initialized. Username: {auth.Username}, Identity length: {auth.Identity?.Length ?? 0}");
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
                else
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
                var clientEnv = ClientAuthSession.Generate(PlayerAuth.GetIdentity());
                _connections[server] = new ServerConnection
                {
                    Session = clientEnv
                };

                var clientHandshake = clientEnv.CreateHandshake();

                writer.PutBytesWithLength(clientHandshake.Encode());
                _phase2Sent = false;
            }
            else
            {
                var attempt = connection.Session.CreateLoginAttempt(connection.Handshake);
                writer.PutBytesWithLength(attempt.Encode());
                writer.Put(connection.ServerIdentifier, 0);
                writer.Put(PlayerAuth.Username, 0);

                CurrentKey = connection.Session.GetSharedSecret(connection.Handshake);
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
        var requestType = data.GetByte();
        if (requestType != 100)
        {
            AuthStatusOverlay.EndAuthenticating();
            ConnectionInfoOverlay.EndOverride();
            MelonLogger.Warning($"Server rejected with code {requestType} (expected 100 for handshake). Passing to base handler.");
            info.AdditionalData._position = pos;
            return true; // Not our rejection, let original handler process it
        }

        var encrypted = data.GetBytesWithLength();
        MelonLogger.Msg($"[Anomaly] ServerHandshake raw bytes ({encrypted.Length}): {BitConverter.ToString(encrypted, 0, Math.Min(encrypted.Length, 16))}...");
        ServerHandshake handShake;
        try
        {
            handShake = ServerHandshake.Decode(encrypted);
        }
        catch (AnomalyProtocolVersionMismatchException pv)
        {
            AuthStatusOverlay.EndAuthenticating();
            ConnectionInfoOverlay.EndOverride();
            MelonLogger.Error($"Server speaks a different Anomaly protocol version. {pv.Message}");
            return false;
        }
        MelonLogger.Msg($"[Anomaly] ServerHandshake sessionPublic[0..3]: {BitConverter.ToString(handShake.sessionPublic, 0, 4)}, challenge[0..3]: {BitConverter.ToString(handShake.challenge, 0, 4)}");
        var server = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.clientAddress;

        if (!_connections.TryGetValue(server, out var connection))
        {
            MelonLogger.Error("Got an Handshake from a Server eventough no ClientSession exist");
            return false;
        }
        connection.Handshake = handShake;
        connection.ServerIdentifier = data.GetString();
        MelonLogger.Msg($"[Anomaly] Stored handshake for server {server}, identifier: {connection.ServerIdentifier}");

        ConnectionInfoOverlay.BeginOverride(Tr.Get(TrKeys.Overlay_ConnectionAuthenticating));
        MelonCoroutines.Start(ConnectAgain(server, Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.port.ToString()));
        return false; // We handled it, skip original handler
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
            Username = string.IsNullOrEmpty(name) ? Computer.UserName : name,
        };
        auth.SetIdentity(AuthCrypto.CreateIdentity());

        File.Create(AuthFilePath).Close();
        File.WriteAllText(AuthFilePath, JsonConvert.SerializeObject(auth));

        return auth;
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
            _connectionGeneration++;
            _phase2Sent = false;
        }

        MelonLogger.Msg("OnDisconnected");
    }
}
