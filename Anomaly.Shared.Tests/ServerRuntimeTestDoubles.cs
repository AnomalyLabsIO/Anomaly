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
    public Mirror.NetworkConnectionToClient connectionToClient { get; set; } =
        new Mirror.NetworkConnectionToClient { isReady = true };
}

namespace Anomaly.Server.Content
{
    internal static class ServerContentRegistrar
    {
        public static bool RegisterItems(IEnumerable<Anomaly.Shared.Content.CatalogEntry> entries) => true;
        public static bool Reset() => true;
    }

    public sealed class CustomItemDefinition
    {
        public string Key { get; set; }
        public InventorySystem.ItemType BaseTemplate { get; set; } = InventorySystem.ItemType.Coin;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconPath { get; set; }
        public ItemLogicBase Logic { get; set; }
    }

    public abstract class ItemLogicBase
    {
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

namespace Anomaly.Server.Assets
{
    internal static class AssetRegistry
    {
        public static byte[] GetHashByPath(string path) => null;
    }
}

public static class ReferenceHubStressTestExtensions
{
    public static string LoggedNameFromRefHub(this ReferenceHub hub) => "stress-test-peer";
}

namespace Anomaly.Server
{
    internal static class Plugin
    {
        public static int QuarantineCount { get; private set; }
        public static ReferenceHub LastQuarantinedHub { get; private set; }
        public static string LastQuarantineContext { get; private set; }

        public static void QuarantineAuthenticatedPlayer(ReferenceHub hub, string context)
        {
            QuarantineCount++;
            LastQuarantinedHub = hub;
            LastQuarantineContext = context;
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
    public sealed class NetworkConnectionToClient
    {
        public bool isReady;

        public void Disconnect()
        {
            isReady = false;
        }
    }
}

namespace MEC
{
    public struct CoroutineHandle
    {
        public bool IsRunning { get; internal set; }
    }

    public static class Timing
    {
        public static float WaitForOneFrame => 0f;
        public static bool ThrowOnKill { get; set; }
        public static bool ThrowOnRun { get; set; }

        public static float WaitForSeconds(float seconds) => seconds;

        public static CoroutineHandle RunCoroutine(IEnumerator<float> routine)
        {
            if (ThrowOnRun)
                throw new InvalidOperationException("injected coroutine-start failure");
            return new CoroutineHandle { IsRunning = routine != null };
        }

        public static void KillCoroutines(CoroutineHandle handle)
        {
            if (ThrowOnKill)
                throw new InvalidOperationException("injected coroutine-cancellation failure");
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
    internal static class AnomalyMessaging
    {
        public static readonly List<IAnomalyMessage> Sent = new List<IAnomalyMessage>();
        public static Func<IAnomalyMessage, bool> TrySendResult =
            _ => true;
        public static Func<IAnomalyMessage, Exception> TrySendException =
            _ => null;

        public static void SendToClient(ReferenceHub hub, IAnomalyMessage message)
            => Sent.Add(message);

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
            Sent.Clear();
            TrySendResult = _ => true;
            TrySendException = _ => null;
        }
    }
}

namespace Anomaly.Server.FileTransfer
{
    internal static class ServerAssetManager
    {
        private static readonly Dictionary<string, AssetInfo> Assets =
            new Dictionary<string, AssetInfo>(StringComparer.Ordinal);

        public static void SetAssets(params AssetInfo[] assets)
        {
            Assets.Clear();
            foreach (AssetInfo asset in assets)
                Assets[ToHex(asset.Hash)] = asset;
        }

        public static FileManifestMessage BuildManifest(
            ulong sessionId,
            out Dictionary<string, AssetInfo> snapshot)
        {
            snapshot = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
            var message = new FileManifestMessage { SessionId = sessionId };
            foreach (KeyValuePair<string, AssetInfo> pair in Assets)
            {
                AssetInfo asset = pair.Value;
                asset.Hash = (byte[])asset.Hash.Clone();
                snapshot.Add(pair.Key, asset);
                message.Files.Add(new FileManifestMessage.FileEntry(
                    asset.RelativePath,
                    (byte[])asset.Hash.Clone(),
                    asset.Size,
                    asset.Kind));
            }
            message.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(message.Files);
            return message;
        }

        public static string ToHex(byte[] hash)
            => BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();

        public static byte[] ComputeCurrentManifestSha256()
            => FileManifestMessage.ComputeManifestSha256(Array.Empty<FileManifestMessage.FileEntry>());

        public static bool TryGetAsset(byte[] hash, out AssetInfo asset)
        {
            asset = default;
            return hash != null && Assets.TryGetValue(ToHex(hash), out asset);
        }

        public struct AssetInfo
        {
            public string FullPath;
            public string RelativePath;
            public byte[] Hash;
            public uint Size;
            public AssetKind Kind;
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

        public static bool HasFile(byte[] hash) => false;

        public static void Evict(long maximumBytes)
        {
        }

        public static bool ImportVerifiedFile(
            string tempPath,
            byte[] expectedHash,
            string originalPath,
            uint expectedSize)
        {
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
        }
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
    }

    internal static class FileTransferEvents
    {
        public static bool ThrowOnComplete { get; set; }

        public static void OnProgress(
            Anomaly.Client.Api.Events.Args.FileTransferProgressEventArgs args)
        {
        }

        public static void OnComplete(
            Anomaly.Client.Api.Events.Args.FileTransferCompleteEventArgs args)
        {
            if (ThrowOnComplete)
                throw new InvalidOperationException("injected completion-event failure");
        }
    }
}

namespace Anomaly.Client.Api.Internal.Localization
{
    internal static class Tr
    {
        public static string Get(string key, params object[] arguments) => key;
    }
}

namespace Anomaly.Client.Api.Localization
{
    internal static class TrKeys
    {
        public const string FileTransfer_Connecting = "file-transfer-connecting";
        public const string FileTransfer_LoadingLevel = "file-transfer-loading";
    }
}

namespace Anomaly.Client.Api.Networking
{
    internal static class AnomalyMessaging
    {
        public static event Action<IAnomalyMessage> MessageReceived
        {
            add { }
            remove { }
        }
        public static readonly List<IAnomalyMessage> Sent = new List<IAnomalyMessage>();

        public static void Send(IAnomalyMessage message) => Sent.Add(message);

        public static bool TrySend(IAnomalyMessage message)
        {
            Sent.Add(message);
            return true;
        }

        public static void ResetForTests() => Sent.Clear();
    }
}

namespace Anomaly.Client.Components
{
    internal static class FileTransferOverlay
    {
        public static bool ThrowOnHide { get; set; }

        public static void Show()
        {
        }

        public static void Hide()
        {
            if (ThrowOnHide)
                throw new InvalidOperationException("injected overlay failure");
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
    internal static class NetworkServer
    {
        public static bool active => false;
    }
}

namespace MelonLoader
{
    internal static class MelonLogger
    {
        public static bool ThrowOnError { get; set; }
        public static bool ThrowOnWarning { get; set; }

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
        }
    }

    internal static class MelonCoroutines
    {
        public static object Start(IEnumerator routine) => new object();
    }
}

namespace UnityEngine
{
    internal sealed class WaitForSeconds
    {
        public WaitForSeconds(float seconds)
        {
        }
    }
}
