using System.Linq;
using System.Collections;
using System.Security.Cryptography;
using System.Threading;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;

// These adapters let pure state and bounded-queue code from Anomaly.Server and
// Anomaly.Client run in the test host. They deliberately do not emulate Unity,
// Mirror networking, LabAPI, or MEC scheduling.

public sealed class ReferenceHub
{
    public uint netId { get; set; }
    private Mirror.NetworkConnectionToClient _connection;
    public ReferenceHub() { connectionToClient = new Mirror.NetworkConnectionToClient { isReady = true }; }
    public Mirror.NetworkConnectionToClient connectionToClient
    {
        get => _connection;
        set { _connection = value; if (value != null) value.Owner = this; }
    }
}

namespace Anomaly.Server.Content
{
    internal static class ServerContentRegistrar
    {
        internal static readonly List<Anomaly.Shared.Content.CatalogEntry> RegisteredItems = new();
        internal static bool AcceptItems = true;
        public static bool RegisterItems(IEnumerable<Anomaly.Shared.Content.CatalogEntry> entries)
        {
            if (!AcceptItems) return false;
            RegisteredItems.AddRange(entries);
            return true;
        }
        public static bool Reset() { RegisteredItems.Clear(); return true; }
    }

}

namespace InventorySystem
{
    public enum ItemType
    {
        Coin = 0,
    }

    public static class InventoryItemLoader
    {
        public static Dictionary<ItemType, object> AvailableItems { get; } =
            new Dictionary<ItemType, object>();
    }
}

namespace Anomaly.Server.Api.Assets
{
    internal static class AssetRegistry
    {
    }
}

namespace Anomaly.Server.Api.Content
{
    internal static class CustomItems
    {
        internal static int ReadyPublications;
        internal static bool RaisePlayerReady(ReferenceHub hub) { ReadyPublications++; return true; }
    }
}

public static class ReferenceHubStressTestExtensions
{
    public static string LoggedNameFromRefHub(this ReferenceHub hub) => "stress-test-peer";
}

// Native scheduling is covered in the Server boundary suite. Shared tests only
// need the exit-idle boundary used when an authenticated admission begins.
public static class IdleMode
{
    public static void SetIdleMode(bool state) { }
}

namespace Anomaly.Server
{
    internal static class Plugin
    {
        internal static bool IsAuthenticatedAdmissionCurrent(ReferenceHub hub) =>
            Anomaly.Server.Networking.AuthenticatedTransportSessions.IsActive(hub);
        public static int QuarantineCount { get; private set; }
        public static ReferenceHub LastQuarantinedHub { get; private set; }
        public static string LastQuarantineContext { get; private set; }

        public static void QuarantineAuthenticatedPlayer(ReferenceHub hub, string context)
        {
            QuarantineCount++;
            LastQuarantinedHub = hub;
            LastQuarantineContext = context;
            Anomaly.Server.Networking.AuthenticatedTransportSessions.Quarantine(hub);
        }

        public static void ResetForTests()
        {
            QuarantineCount = 0;
            LastQuarantinedHub = null;
            LastQuarantineContext = null;
        }
    }
}

namespace Mirror
{
    public class NetworkConnectionToClient
    {
        public int connectionId;
        internal ReferenceHub Owner;
        internal bool Rejected;
        public bool isReady;
        public int DisconnectCount { get; private set; }

        public void Disconnect()
        {
            DisconnectCount++;
            isReady = false;
        }
    }
    public sealed class LocalConnectionToClient : NetworkConnectionToClient { }
}

public sealed class EncryptedChannelManager
{
    public enum SecurityLevel { Unsecured, EncryptedAndAuthenticated }
    public readonly struct EncryptedMessageOutside
    {
        public EncryptedMessageOutside(SecurityLevel level, byte[] data) { Level = level; Data = data; }
        public SecurityLevel Level { get; }
        public byte[] Data { get; }
    }
}
namespace NetworkManagerUtils.Dummies { public sealed class DummyNetworkConnection : Mirror.NetworkConnectionToClient { } }

namespace MEC
{
    public struct CoroutineHandle
    {
        public bool IsRunning { get; internal set; }
        internal int Id;
    }

    public static class Timing
    {
        private static int _nextId;
        private static readonly Dictionary<int, IEnumerator<float>> Routines = new Dictionary<int, IEnumerator<float>>();
        public static float WaitForOneFrame => 0f;
        public static bool ThrowOnKill { get; set; }
        public static bool ThrowOnRun { get; set; }

        public static float WaitForSeconds(float seconds) => seconds;

        public static CoroutineHandle RunCoroutine(IEnumerator<float> routine)
        {
            if (ThrowOnRun)
                throw new InvalidOperationException("injected coroutine-start failure");
            int id = ++_nextId; Routines[id] = routine; return new CoroutineHandle { IsRunning = routine != null, Id = id };
        }

        public static void RunPendingStarts()
        {
            foreach (var entry in Routines.ToArray())
            {
                if (!entry.Value.GetType().Name.Contains("StartWhenReady") && !entry.Value.GetType().Name.Contains("Watch")) continue;
                for (int step = 0; step < 4 && entry.Value.MoveNext(); step++) { }
                Routines.Remove(entry.Key);
            }
        }
        public static void KillCoroutines(CoroutineHandle handle)
        {
            if (ThrowOnKill)
                throw new InvalidOperationException("injected coroutine-cancellation failure");
            if (Routines.TryGetValue(handle.Id, out var routine)) { Routines.Remove(handle.Id); routine.Dispose(); }
        }
    }
}

namespace LabApi.Features.Console
{
    public static class Logger
    {
        public static void Raw(string message, ConsoleColor color)
        {
        }

        public static void Info(string message)
        {
        }

        public static void Warn(string message)
        {
        }

        public static void Error(string message)
        {
        }
    }
}

namespace Anomaly.Server.Auth
{
    internal static class RandomGenerator
    {
        private static long _next;

        public static string GetStringSecure(int length)
        {
            string value = Interlocked.Increment(ref _next).ToString();
            return value.PadLeft(length, '0');
        }
    }
}

namespace Anomaly.Server.Networking
{
    internal static class ConnectionBootstrap
    {
        internal static void Reject(Mirror.NetworkConnectionToClient connection, string reason)
        {
            if (connection == null || connection.Rejected) return;
            connection.Rejected = true;
            ConnectionContentBootstrap.MarkRejected(connection);
            AuthenticatedTransportSessions.QuarantineConnection(connection);
            ConnectionContentBootstrap.Cleanup(connection);
            Anomaly.Server.Plugin.QuarantineAuthenticatedPlayer(connection.Owner, reason);
            connection.Disconnect();
            AuthenticatedTransportSessions.RemoveConnection(connection);
        }
    }

    internal static class AnomalyMessaging
    {
        public static event Action<ReferenceHub, IAnomalyMessage> MessageReceived;
        public static void RaiseForTests(ReferenceHub hub, IAnomalyMessage message) => MessageReceived?.Invoke(hub, message);
        public static readonly List<IAnomalyMessage> Sent = new List<IAnomalyMessage>();
        public static Func<IAnomalyMessage, bool> TrySendResult =
            _ => true;
        public static Func<IAnomalyMessage, Exception> TrySendException =
            _ => null;
        internal static Func<FileChunkMessage, Anomaly.Server.FileTransfer.BulkSendResult> BulkSendResult =
            _ => Anomaly.Server.FileTransfer.BulkSendResult.Sent;
        internal static Func<Mirror.NetworkConnectionToClient, (int Mtu, int RoundTripTime)> BulkLinkMetrics = _ => (1024, 100);

        public static void SendToClient(ReferenceHub hub, IAnomalyMessage message)
            => Sent.Add(message);
        public static bool TrySendToConnection(Mirror.NetworkConnectionToClient connection, IAnomalyMessage message) =>
            TrySendToClient(connection.Owner, message);

        internal static bool TryGetBulkTransferProfile(out byte version, out ulong laneMask)
        { version = BulkTransferProtocol.ProfileVersion; laneMask = ((1UL << 16) - 1) << 6; return true; }
        internal static bool TryGetBulkLinkCapacity(Mirror.NetworkConnectionToClient connection, ulong laneMask,
            out int mtu, out int roundTripTime)
        {
            var metrics = BulkLinkMetrics(connection);
            mtu = metrics.Mtu; roundTripTime = metrics.RoundTripTime;
            return mtu > 10 && roundTripTime >= 0;
        }
        internal static Anomaly.Server.FileTransfer.BulkSendResult TrySendBulkChunkToConnection(
            Mirror.NetworkConnectionToClient connection, FileChunkMessage message, int domain)
        {
            var result = BulkSendResult(message);
            if (result != Anomaly.Server.FileTransfer.BulkSendResult.Sent) return result;
            var replay = AuthenticatedTransportSessions.GetBulkReplayState(connection);
            if (replay == null) return Anomaly.Server.FileTransfer.BulkSendResult.Failed;
            message.FrameCounter = replay.NextTxCounter(domain);
            return TrySendToConnection(connection, message) ? Anomaly.Server.FileTransfer.BulkSendResult.Sent :
                Anomaly.Server.FileTransfer.BulkSendResult.Failed;
        }

        public static bool TrySendToClient(ReferenceHub hub, IAnomalyMessage message)
        {
            Exception exception = TrySendException(message);
            if (exception != null)
                throw exception;
            bool delivered = TrySendResult(message);
            if (delivered)
                Sent.Add(message);
            return delivered;
        }

        public static void ResetForTests()
        {
            ConnectionContentBootstrap.Reset();
            AuthenticatedTransportSessions.Reset();
            Sent.Clear();
            TrySendResult = _ => true;
            TrySendException = _ => null;
            BulkSendResult = _ => Anomaly.Server.FileTransfer.BulkSendResult.Sent;
            BulkLinkMetrics = _ => (1024, 100);
        }
    }
}

namespace Anomaly.Server.FileTransfer
{
    // Deterministic publisher boundary: the real snapshot copier and chunk reader are linked below.
    internal static class ServerAssetManager
    {
        internal static readonly object Sync = new object();
        internal static void ValidateFreezeSnapshot(AssetPackSnapshot snapshot) { }
        private static AssetPackSnapshot _snapshot = AssetPackSnapshot.Empty();
        private static string _sourceRoot;
        public static bool IsReady => true;
        public static void Rescan() { }
        internal static void InvalidateContentRegistrations() { }
        public static void SetAssets(params AssetInfo[] assets)
        {
            _snapshot.Dispose();
            _snapshot = AssetPackSnapshot.Empty();
            if (_sourceRoot != null)
            {
                string spool = Path.Combine(_sourceRoot, ".anomaly-spool");
                if (!SpinWait.SpinUntil(() => !Directory.Exists(spool) || !Directory.EnumerateDirectories(spool).Any(), 5000))
                    throw new InvalidOperationException("Fixture snapshot was still leased during test cleanup.");
                Directory.Delete(_sourceRoot, true);
                _sourceRoot = null;
            }
            if (assets.Length == 0) return;
            _sourceRoot = Path.Combine(Path.GetTempPath(), "anomaly-transfer-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_sourceRoot);
            for (int i = 0; i < assets.Length; i++)
            {
                AssetInfo file = assets[i];
                string packRoot = Path.Combine(_sourceRoot, "fixture", "pack" + i);
                Directory.CreateDirectory(packRoot);
                string name = "fixture" + i + ".bundle";
                File.Copy(file.FullPath, Path.Combine(packRoot, name));
                var index = new AssetPackIndex
                {
                    SchemaVersion = AssetPackIndex.CurrentSchemaVersion, PackId = "fixture:pack" + i, Namespace = "fixture",
                    UnityVersion = AssetPackIndex.SupportedUnityVersion,
                    Platform = AssetPackIndex.SupportedPlatform,
                    GameProfile = AssetPackIndex.SupportedGameProfile,
                    BundleFile = name, BundleSha256 = ToHex(file.Hash), BundleBytes = file.Size, DecodedBytes = 192000,
                    Assets = new List<PackAssetEntry>
                    {
                        new PackAssetEntry { Address = "mods/fixture/audio/test", EntryName = "mods/fixture/audio/test", Kind = "AudioClip", SampleFrames = 48000, SampleRate = 48000, Channels = 1 }
                    },
                };
                var json = Newtonsoft.Json.Linq.JObject.FromObject(index);
                foreach (var asset in json["Assets"]) ((Newtonsoft.Json.Linq.JObject)asset).Remove("AssetKind");
                File.WriteAllText(Path.Combine(packRoot, "fixture" + i + ".index.json"), json.ToString());
                File.WriteAllText(Path.Combine(packRoot, "pack.json"), "{\"SchemaVersion\":1,\"PackId\":\"fixture:pack" + i +
                    "\",\"Version\":\"1.0.0\",\"Assets\":{\"PackId\":\"fixture:pack" + i + "\",\"Index\":\"fixture" + i + ".index.json\"}}");
            }
            _snapshot.Dispose();
            _snapshot = AssetPackSnapshot.Build(_sourceRoot);
        }
        public static AssetPackSnapshot AcquireSnapshot() => _snapshot.Retain();
        public static bool TryAcquireOfferSnapshot(out AssetPackSnapshot snapshot) { snapshot = _snapshot.Retain(); return true; }
        public static bool TryGetAsset(AssetAddress address, out AssetPackSnapshot.AssetRecord asset) => _snapshot.TryGetAsset(address, out asset);
        public static byte[] ComputeCurrentManifestSha256() => _snapshot.BuildManifest(1).ManifestSha256;
        public static string ToHex(byte[] hash) => BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        internal struct AssetInfo
        {
            public string FullPath;
            public string RelativePath;
            public byte[] Hash;
            public uint Size;
            public FileContentKind Kind;
        }
    }
}
namespace Anomaly.Client.Api.Features
{
    internal static class Paths
    {
        public static string FileCachePath { get; set; } = Path.Combine(
            Path.GetTempPath(),
            "Anomaly-Shared-Tests",
            Guid.NewGuid().ToString("N"));
    }
}

namespace Anomaly.Client.Api.Assets
{
    internal static class AnomalyResources
    {
        public static void SetRemoteAssetAuthorization(Func<byte[], AssetKind, bool> authorization)
        {
        }
    }
}

namespace Anomaly.Client.Api.FileTransfer
{
    internal static class FileCache
    {
        public static bool ThrowOnImport { get; set; }

        public static string ToHex(byte[] hash)
            => BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();

        public static Func<byte[], bool> HasFileResult { get; set; } = _ => false;
        public static bool HasFile(byte[] hash) => HasFileResult(hash);

        public static void Evict(long maximumBytes)
        {
        }

        public static bool ImportVerifiedFile(
            string tempPath,
            byte[] expectedHash,
            string originalPath,
            uint expectedSize,
            System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnImport)
                throw new IOException("injected cache-import failure");
            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length != expectedSize)
                return false;

            bool matches;
            using (var stream = File.OpenRead(tempPath))
            using (var sha = SHA256.Create())
            {
                matches = Anomaly.Shared.Auth.PasswordAuth.FixedTimeEquals(
                    sha.ComputeHash(stream),
                    expectedHash);
            }
            if (!matches)
                return false;

            File.Delete(tempPath);
            return true;
        }
    }
}

namespace Anomaly.Client.Api.Events.Args
{
    internal sealed class ClientConnectedEventArgs
    {
    }

    internal sealed class ClientReadyEventArgs
    {
    }

    internal sealed class FileTransferProgressEventArgs
    {
        public FileTransferProgressEventArgs(
            string path,
            int completedFiles,
            int totalFiles,
            long receivedBytes,
            long totalBytes,
            float progress)
        {
        }
    }

    internal sealed class FileTransferCompleteEventArgs
    {
        public FileTransferCompleteEventArgs(bool succeeded, int completedFiles)
        {
            Success = succeeded;
            FileCount = completedFiles;
        }
        internal bool Success { get; }
        internal int FileCount { get; }
    }
}

namespace Anomaly.Client.Api.Events.Handlers
{
    internal static class ClientEvents
    {
        public static event Action<Anomaly.Client.Api.Events.Args.ClientConnectedEventArgs> Connected
        {
            add { }
            remove { }
        }

        public static event Action<Anomaly.Client.Api.Events.Args.ClientReadyEventArgs> Ready
        {
            add { }
            remove { }
        }

        public static event Action<Anomaly.Client.Api.Events.Args.ClientDisconnectedEventArgs> Disconnected
        {
            add { }
            remove { }
        }
    }

    internal static class FileTransferEvents
    {
        public static bool ThrowOnComplete { get; set; }
        internal static Action<Anomaly.Client.Api.Events.Args.FileTransferCompleteEventArgs> CompleteObserved;

        public static void OnProgress(
            Anomaly.Client.Api.Events.Args.FileTransferProgressEventArgs args)
        {
        }

        public static void OnComplete(
            Anomaly.Client.Api.Events.Args.FileTransferCompleteEventArgs args)
        {
            if (ThrowOnComplete)
                throw new InvalidOperationException("injected completion-event failure");
            CompleteObserved?.Invoke(args);
        }
    }
}

namespace Anomaly.Client.Api.Localization
{
    internal static class Tr
    {
        public static string Get(string key, params object[] arguments) => key;
    }
}

namespace Anomaly.Client.Api.Networking
{
    internal static class AnomalyMessaging
    {
        internal static Action<IAnomalyMessage> ContentBootstrapMessageReceived;
        internal static Func<bool> BulkReceiveExpected;
        internal static Func<ulong, bool> BulkSessionExpected;
        internal static Action TransportSessionRevoked;
        internal static long TransportGeneration { get; private set; }
        public static event Action<IAnomalyMessage> MessageReceived
        {
            add { }
            remove { }
        }
        public static readonly List<IAnomalyMessage> Sent = new List<IAnomalyMessage>();
        public static readonly List<string> TransportEvents = new List<string>();
        public static Func<IAnomalyMessage, bool> TrySendResult = _ => true;
        public static Func<IAnomalyMessage, Exception> TrySendException = _ => null;
        public static int SessionRevocations;

        public static void Send(IAnomalyMessage message) => Sent.Add(message);

        public static bool TrySend(IAnomalyMessage message)
        {
            Sent.Add(message);
            if (message is ContentCatalogAckMessage acknowledgement && acknowledgement.Status == ContentCatalogActivationStatus.Failed)
                TransportEvents.Add("ack");
            Exception exception = TrySendException(message);
            if (exception != null) throw exception;
            return TrySendResult(message);
        }

        public static void ResetForTests()
        {
            Sent.Clear(); TransportEvents.Clear();
            TrySendResult = _ => true; TrySendException = _ => null;
            SessionRevocations = 0; TransportFailures = 0;
            Il2CppMirror.NetworkClient.connection = null;
            UnityEngine.Time.realtimeSinceStartupAsDouble = 0;
            MelonLoader.MelonCoroutines.ResetForTests();
        }

        public static int TransportFailures { get; set; }
        public static long ClearTransportSession()
        {
            TransportGeneration = checked(TransportGeneration + 1);
            long generation = TransportGeneration;
            SessionRevocations++; TransportEvents.Add("revoke");
            if (Anomaly.Client.Auth.AuthHandler.UsingAnomalyAuth) TransportSessionRevoked?.Invoke();
            return generation;
        }
        public static void FailClosedTransport(string reason)
        {
            TransportFailures++;
            ClearTransportSession();
            Il2CppMirror.NetworkClient.connection?.Disconnect();
        }
    }
}

namespace Anomaly.Client.Components
{
    internal static class FileTransferOverlay
    {
        public static bool ThrowOnHide { get; set; }
        public static bool Visible { get; private set; }

        public static void Show()
        {
            Visible = true;
        }

        public static void Hide()
        {
            if (ThrowOnHide)
                throw new InvalidOperationException("injected overlay failure");
            Visible = false;
        }

        public static void SetStatus(string status)
        {
        }

        public static void UpdateProgress(
            string path,
            int currentFile,
            int totalFiles,
            float progress)
        {
        }
    }
}

namespace Il2CppMirror
{
    internal class NetworkConnection
    {
        internal IntPtr Pointer { get; set; } = new IntPtr(1);
        internal bool ThrowOnUpdate;
        internal int Updates, Disconnects;
        internal void Update()
        {
            Updates++;
            Anomaly.Client.Api.Networking.AnomalyMessaging.TransportEvents.Add("flush");
            if (ThrowOnUpdate) throw new InvalidOperationException("injected Mirror flush failure");
        }
        internal void Disconnect()
        {
            Disconnects++;
            Anomaly.Client.Api.Networking.AnomalyMessaging.TransportEvents.Add("disconnect");
        }
    }
    internal static class NetworkClient
    {
        internal static NetworkConnection connection;
        internal static bool activeHost;
        internal static int ReadyCalls, AddPlayerCalls;
        internal static bool Ready() { ReadyCalls++; return true; }
        internal static bool AddPlayer() { AddPlayerCalls++; return true; }
    }
    internal static class NetworkServer
    {
        public static bool active;
    }
}

namespace MelonLoader
{
    internal static class MelonLogger
    {
        public static bool ThrowOnError { get; set; }
        public static bool ThrowOnWarning { get; set; }
        internal static Action<string> ErrorObserved;

        public static void Msg(string message)
        {
        }

        public static void Warning(string message)
        {
            if (ThrowOnWarning)
                throw new InvalidOperationException("injected warning-logger failure");
        }

        public static void Error(string message)
        {
            if (ThrowOnError)
                throw new InvalidOperationException("injected logger failure");
            ErrorObserved?.Invoke(message);
        }
    }

    internal static class MelonCoroutines
    {
        internal static readonly List<IEnumerator> Started = new();
        internal static bool ReturnNull, ThrowOnStart;
        public static object Start(IEnumerator routine)
        {
            Anomaly.Client.Api.Networking.AnomalyMessaging.TransportEvents.Add("schedule");
            if (ThrowOnStart) throw new InvalidOperationException("injected coroutine scheduling failure");
            if (ReturnNull) return null;
            Started.Add(routine);
            return new object();
        }
        internal static void ResetForTests() { Started.Clear(); ReturnNull = ThrowOnStart = false; }
    }
}

namespace UnityEngine
{
    internal static class Time { internal static double realtimeSinceStartupAsDouble; }
    internal sealed class WaitForSeconds
    {
        public WaitForSeconds(float seconds)
        {
        }
    }
}
