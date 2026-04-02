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
using Anomaly.Client.API.Features;
using Anomaly.Client.Components;
using Anomaly.Shared.Auth;
using Il2CppMEC;
using Newtonsoft.Json;
using UnityEngine;
using Action = Il2CppSystem.Action;

namespace Anomaly.Client.Auth;

public static class AuthHandler
{
    private static Dictionary<string, ServerConnection> _connections = new();
    
    public static byte[] CurrentKey { get; private set; }

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
    private static int _connectionGeneration;

    internal static void AuthWrite(NetDataWriter writer)
    {
        try
        {
            MelonLogger.Msg("AuthWrite called");
            var server = Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport.Singleton.clientAddress;

            // If _authWriteCalled is still true but the connection state for this server
            // was already consumed (removed after phase 2), this is a fresh connection
            // attempt (e.g. after round restart disconnect). Reset the flag.
            if (_authWriteCalled && !_connections.ContainsKey(server))
            {
                MelonLogger.Msg("Resetting auth state for fresh connection attempt (previous session completed or expired)");
                _authWriteCalled = false;
            }

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
            }
            else
            {
                _connections.Remove(server);
                var attempt = connection.Session.CreateLoginAttempt(connection.Handshake);
                writer.PutBytesWithLength(attempt.Encode());
                writer.Put(connection.ServerIdentifier);
                writer.Put(PlayerAuth.Username);

                CurrentKey = connection.Session.GetSharedSecret(connection.Handshake);
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
            MelonLogger.Warning($"Server rejected with code {requestType} (expected 100 for handshake). Passing to base handler.");
            info.AdditionalData._position = pos;
            return true; // Not our rejection, let original handler process it
        }

        var encrypted = data.GetBytesWithLength();
        MelonLogger.Msg($"[Anomaly] ServerHandshake raw bytes ({encrypted.Length}): {BitConverter.ToString(encrypted, 0, Math.Min(encrypted.Length, 16))}...");
        var handShake = ServerHandshake.Decode(encrypted);
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
        AuthStatusOverlay.EndAuthenticating();
        MelonLogger.Msg("OnDisconnected");
    }
}
