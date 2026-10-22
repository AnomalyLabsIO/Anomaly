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
        public EncryptedMessageOutside(SecurityLevel level, byte[] data) { Level = level; Data = data; }
        public SecurityLevel Level { get; }
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
    public static class NetworkTime { public static double localTime => 1d; }
    public sealed class NetworkWriterPooled : IDisposable
    {
        internal MemoryStream Stream { get; } = new();
        public int Position => (int)Stream.Length;
        public ArraySegment<byte> ToArraySegment() => new(Stream.ToArray());
        public void Dispose() => Stream.Dispose();
    }
    public static class NetworkWriterPool { public static NetworkWriterPooled Get() => new(); }
    public static class NetworkMessages
    {
        public static void Pack(EncryptedChannelManager.EncryptedMessageOutside message, NetworkWriterPooled writer)
        {
            // Inert framing boundary; tests inspect routing and packet bounds, not Mirror's codec.
            var binary = new BinaryWriter(writer.Stream);
            binary.Write((ushort)9934);
            binary.Write((byte)message.Level);
            binary.Write(message.Data.Length);
            binary.Write(message.Data);
        }
    }
    public sealed class Batcher
    {
        private ArraySegment<byte> _message;
        public Batcher(int threshold) { }
        public void AddMessage(ArraySegment<byte> message, double timestamp) => _message = message;
        public bool GetBatch(NetworkWriterPooled writer)
        {
            var binary = new BinaryWriter(writer.Stream);
            binary.Write(1d);
            binary.Write(_message.Count);
            binary.Write(_message.Array, _message.Offset, _message.Count);
            return true;
        }
    }
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
        public int MaximumPacketBytes { get; set; } = int.MaxValue;
        public LiteNetLib.DeliveryMethod[] channels { get; } =
        {
            LiteNetLib.DeliveryMethod.ReliableOrdered, LiteNetLib.DeliveryMethod.Unreliable,
            LiteNetLib.DeliveryMethod.Sequenced, LiteNetLib.DeliveryMethod.ReliableSequenced,
            LiteNetLib.DeliveryMethod.ReliableUnordered,
        };
        public int GetMaxPacketSize(int channel) => MaximumPacketBytes;
    }
    public sealed class Peer
    {
        public IPEndPoint EndPoint { get; set; }
        public LiteNetLib.ConnectionState ConnectionState { get; set; } = LiteNetLib.ConnectionState.Connected;
        public int Mtu { get; set; } = 1200;
        public int ReliableQueuedPackets { get; set; }
        public bool AcceptSends { get; set; } = true;
        public List<byte[]> SentPackets { get; } = new();
        public int GetMaxSinglePacketSize(LiteNetLib.DeliveryMethod delivery) =>
            Mtu - (delivery == LiteNetLib.DeliveryMethod.Unreliable ? 1 : 4);
        public int GetPacketsCountInReliableQueue(byte channel, bool ordered) =>
            channel == 0 ? ReliableQueuedPackets : 0;
    }
    public static class LiteNetLib4MirrorServer
    {
        public static Dictionary<int, Peer> Peers { get; } = new();
        public static bool Send(int connectionId, LiteNetLib.DeliveryMethod delivery,
            byte[] data, int start, int length, byte channel)
        {
            if (!Peers.TryGetValue(connectionId, out var peer) || !peer.AcceptSends ||
                peer.ConnectionState != LiteNetLib.ConnectionState.Connected)
                return false;
            peer.SentPackets.Add(data.Skip(start).Take(length).ToArray());
            return true;
        }
    }
}

namespace LiteNetLib
{
    public enum DeliveryMethod { ReliableUnordered, Sequenced, ReliableOrdered, ReliableSequenced, Unreliable }
    public enum ConnectionState { Connected, Disconnected }
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
