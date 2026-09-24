using System.Net;
using Anomaly.Server.Networking;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;

// Only engine, plugin entry point, and transport boundaries are substituted.
// Dispatch, bootstrap rejection, replay, session keys, and enrollment use source links.
public sealed class ReferenceHub
{
    public bool IsDummy { get; set; }
    public Mirror.NetworkConnectionToClient connectionToClient { get; set; } = new();
    public EncryptedChannelManager encryptedChannelManager { get; } = new();
    public static List<ReferenceHub> AllHubs { get; } = new();
    public string LoggedNameFromRefHub() => "test-peer";
}

public sealed class EncryptedChannelManager
{
    public bool isLocalPlayer;
    public enum SecurityLevel { Unsecured, EncryptedAndAuthenticated }
    public readonly struct EncryptedMessageOutside
    {
        public EncryptedMessageOutside(SecurityLevel level, byte[] data) => Data = data;
        public byte[] Data { get; }
    }
}

public sealed class CustomNetworkManager
{
    public void OnServerDisconnect(Mirror.NetworkConnectionToClient connection) { }
}

namespace GameCore { internal static class BoundaryMarker { } }
namespace UnityEngine { internal static class BoundaryMarker { } }

namespace Mirror
{
    public class NetworkConnection { }
    public class NetworkConnectionToClient : NetworkConnection
    {
        public int connectionId;
        public bool ThrowOnDisconnect;
        public bool Disconnected;
        public Action BeforeDisconnect;
        public List<EncryptedChannelManager.EncryptedMessageOutside> Sent { get; } = new();
        public void Disconnect()
        {
            BeforeDisconnect?.Invoke();
            if (ThrowOnDisconnect) throw new InvalidOperationException("injected disconnect failure");
            Disconnected = true;
        }
        public void Send(EncryptedChannelManager.EncryptedMessageOutside message, int channel) => Sent.Add(message);
    }
    public sealed class LocalConnectionToClient : NetworkConnectionToClient { }
    public static class NetworkServer { }
}

namespace NetworkManagerUtils.Dummies
{
    public sealed class DummyNetworkConnection : Mirror.NetworkConnectionToClient { }
}

namespace Mirror.LiteNetLib4Mirror
{
    public sealed class LiteNetLib4MirrorTransport
    {
        public static LiteNetLib4MirrorTransport Singleton { get; } = new();
        public int GetMaxPacketSize(int channel) => int.MaxValue;
    }
    public sealed class Peer { public IPEndPoint EndPoint { get; set; } }
    public static class LiteNetLib4MirrorServer
    {
        public static Dictionary<int, Peer> Peers { get; } = new();
    }
}

namespace Anomaly.Server
{
    internal static class Plugin
    {
        internal static bool IsAuthenticatedAdmissionCurrent(ReferenceHub hub) =>
            AuthenticatedTransportSessions.IsActive(hub);
        internal static bool TryCommitAuthenticatedAdmission(Action action) { action(); return true; }
        internal static void QuarantineAuthenticatedPlayer(ReferenceHub hub, string context) =>
            AuthenticatedTransportSessions.Quarantine(hub);
    }
}

namespace Anomaly.Server.Api.Events.Handlers
{
    internal static class ClientEvents
    {
        internal static int Messages;
        internal static int Connections;
        internal static void OnMessageReceived(ReferenceHub hub, IAnomalyMessage message) => Messages++;
        internal static void OnConnected(ReferenceHub hub) => Connections++;
    }
}

namespace Anomaly.Server.Auth
{
    internal static class AuthHandler
    {
        internal static bool TryTakePlayerForPeer(IPEndPoint endpoint, object peer, out PlayerStorage player)
        { player = null; return false; }
    }
}

namespace Anomaly.Server.Internal.Map
{
    internal static class FacilityLayoutRuntime
    {
        internal static event Action<FacilitySnapshotMessage> SnapshotFrozen { add { } remove { } }
        internal static event Action<Exception> GenerationRejected { add { } remove { } }
        internal static bool GenerationFailed => false;
        internal static bool TryGetSnapshot(out FacilitySnapshotMessage snapshot) { snapshot = null; return false; }
    }
}

namespace Anomaly.Server.Utils
{
    internal static class OffThreadLogger
    {
        internal static readonly System.Collections.Concurrent.ConcurrentQueue<string> Warnings = new();
        internal static void Error(string message) { }
        internal static void Warn(string message) => Warnings.Enqueue(message);
        internal static void Info(string message) { }
    }
}

namespace Anomaly.Server.Content
{
    internal static class ContentRegistry
    {
        internal static bool TryGetId(string key, out int id) { id = 0; return false; }
    }
}

namespace LabApi.Features.Console
{
    internal static class Logger
    {
        internal static void Warn(string message) { }
        internal static void Error(string message) { }
    }
}
