using System.Collections;
using System.Reflection;
using Anomaly.Shared.Auth;
using Il2Cpp;
using Il2CppMirror;

// Only engine/loader/session boundaries are doubled. The linked production
// patches, Shared crypto and replay/rate-limit implementations run unchanged.
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type, string method, params Type[] arguments) { }
    }
    public sealed class HarmonyPrefix : Attribute { }
    public sealed class HarmonyPostfix : Attribute { }
    public sealed class HarmonyPrepare : Attribute { }
    public sealed class HarmonyTargetMethod : Attribute { }
    public enum HarmonyPatchType { Prefix, Postfix }
    public sealed record Patch(string owner, MethodInfo PatchMethod);
    public sealed class Patches
    {
        public List<Patch> Prefixes = new();
        public List<Patch> Postfixes = new();
    }
    public static class Harmony
    {
        public static readonly Dictionary<MethodBase, Patches> Installed = new();
        public static Patches GetPatchInfo(MethodBase target) => Installed.GetValueOrDefault(target);
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppStructArray<T> : IEnumerable<T>
    {
        private readonly T[] _values;
        public Il2CppStructArray(T[] values) => _values = values;
        public int Length => _values.Length;
        public T this[int index] { get => _values[index]; set => _values[index] = value; }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_values).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace Il2CppSystem
{
    public abstract class Delegate
    {
        public virtual IEnumerable<Delegate> GetInvocationList() => new[] { this };
    }
}

namespace Il2Cpp
{
    public sealed class ReferenceHub
    {
        public EncryptedChannelManager encryptedChannelManager = new();
    }
    public sealed class GameConsoleTransmission
    {
        public bool isLocalPlayer;
        public void Start() { }
    }
    public sealed class EncryptedChannelManager
    {
        public enum EncryptedChannel : byte { GameConsole, RemoteAdmin, AdminChat }
        public enum SecurityLevel : byte { Unsecured, EncryptedAndAuthenticated }
        public readonly record struct EncryptedMessageOutside(byte[] Data, SecurityLevel Level);
        public readonly record struct EncryptedMessage(EncryptedChannel Channel, byte[] Content, uint Counter);
        public sealed class EncryptedMessageClientHandler : Il2CppSystem.Delegate
        {
            private readonly Action<EncryptedMessage, SecurityLevel> _handler;
            public EncryptedMessageClientHandler(Action<EncryptedMessage, SecurityLevel> handler) => _handler = handler;
            public void Invoke(EncryptedMessage message, SecurityLevel level) => _handler(message, level);
        }
        public static readonly Dictionary<EncryptedChannel, EncryptedMessageClientHandler> ClientChannelHandlers = new();
        public uint _txCounter;
        public uint _rxCounter;
        public object EncryptionKey;
        public static int ReceiveEntries;
        public bool TrySendMessageToServer(string content, EncryptedChannel channel) => throw new NotSupportedException();
        public bool TrySendMessageToServer(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> content,
            EncryptedChannel channel) => throw new NotSupportedException();
        public static void ClientReceivePackedMessage(EncryptedMessageOutside packed)
        {
            ReceiveEntries++;
            Anomaly.Client.Patches.Auth.PlayerAuthenticationManagerPatch.OnReceivePackedMessage(packed);
        }
    }
}

namespace Il2CppCentralAuth
{
    public sealed class PlayerAuthenticationManager
    {
        public bool isLocalPlayer;
        public ReferenceHub _hub;
        public void Start() { }
    }
}
namespace Il2CppRemoteAdmin
{
    public sealed class QueryProcessor
    {
        public bool isLocalPlayer;
        public void Start() { }
    }
}
namespace Il2CppGameCore
{
    public static class Misc { public static System.Text.Encoding Utf8Encoding => System.Text.Encoding.UTF8; }
}
namespace MelonLoader
{
    public static class MelonLogger
    {
        public static readonly List<string> Messages = new();
        public static void Msg(string message) => Messages.Add(message);
        public static void Warning(string message) => Messages.Add(message);
        public static void Error(string message) => Messages.Add(message);
    }
}
namespace Anomaly.Client.Auth
{
    public static class AuthHandler { public static bool UsingAnomalyAuth = true; }
}
namespace Anomaly.Client.Content
{
    public static class ConnectionContentBootstrap { public static bool IsAdmitted; }
}
namespace Anomaly.Client.Components
{
    public static class AuthStatusOverlay { public static void EndAuthenticating() { } }
    public static class ConnectionInfoOverlay { public static void EndOverride() { } }
}
namespace Anomaly.Client.Api.Events { }
namespace Anomaly.Client.Api.Features
{
    public sealed class LocalPlayer
    {
        public static LocalPlayer Instance;
        public ReferenceHub ReferenceHub;
    }
}
namespace Anomaly.Client.Api.Internal.Wrappers
{
    public static class PlayerRegistry
    {
        public static bool TryGetLocalHub(out ReferenceHub hub)
        {
            hub = Features.LocalPlayer.Instance?.ReferenceHub;
            return hub != null;
        }
    }
}
namespace Anomaly.Client.Api.Modding
{
    public enum FeatureHealthStatus { Healthy, Disabled }
    public static class FeatureHealth
    {
        public static void Report(string mod, string feature, FeatureHealthStatus status, string detail,
            string expected, string target, string remediation) { }
    }
}
namespace Anomaly.Client.Api.Networking
{
    public static class AnomalyMessaging
    {
        internal static Func<bool> BulkReceiveExpected;
        internal static Func<int, bool> IsBulkPhysicalChannel;
        public static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        public static readonly byte[] SessionId = new byte[16];
        public static readonly byte[] Transcript = new byte[32];
        public static readonly List<string> Failures = new();
        public static readonly List<byte[]> CustomMessages = new();
        public static bool LastBeforeContentReady;
        public static bool HasSession = true;
        public static int DecryptAttempts;
        public static bool TryEncryptClientEnvelope(byte[] data, out byte[] encrypted)
        {
            encrypted = HasSession
                ? AuthTransportCrypto.Encrypt(Key, SessionId, Transcript, AuthTransportDirection.ClientToServer, data)
                : null;
            return encrypted != null;
        }
        public static bool TryDecryptServerEnvelope(byte[] data, out byte[] decrypted)
        {
            DecryptAttempts++;
            decrypted = HasSession
                ? AuthTransportCrypto.Decrypt(Key, SessionId, Transcript, AuthTransportDirection.ServerToClient, data)
                : null;
            return decrypted != null;
        }
        public static void FailClosedTransport(string reason)
        {
            Failures.Add(reason);
            HasSession = false;
        }
        public static void OnReceived(byte[] payload, bool authenticated, bool host, bool beforeContentReady)
        {
            LastBeforeContentReady = beforeContentReady;
            CustomMessages.Add(payload);
        }
    }
    public static class EncryptedMessageTransport
    {
        public static readonly List<byte[]> Attempts = new();
        public static bool DeliveryResult = true;
        public static bool ThrowOnSend;
        public static bool Send(EncryptedChannelManager.SecurityLevel level, byte[] payload, int channel)
        {
            Attempts.Add(payload);
            if (ThrowOnSend) throw new IOException("test delivery failure");
            return DeliveryResult;
        }
    }
}
namespace Il2CppMirror
{
    public static class NetworkServer { public static bool active; }
    public sealed class NetworkConnection { }
    public sealed class NetworkReader
    {
        public EncryptedChannelManager.EncryptedMessageOutside Packed;
        public bool ThrowOnRead;
    }
    public sealed class NetworkMessageDelegate
    {
        private static long _nextPointer;
        private readonly Action<NetworkConnection, NetworkReader, int> _handler;
        public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _nextPointer);
        public NetworkMessageDelegate(Action<NetworkConnection, NetworkReader, int> handler) => _handler = handler;
        public void Invoke(NetworkConnection connection, NetworkReader reader, int channel) => _handler(connection, reader, channel);
    }
    public sealed class HandlerDictionary
    {
        private static long _nextPointer;
        private readonly Dictionary<ushort, NetworkMessageDelegate> _handlers = new();
        public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _nextPointer);
        public bool FailNextPublication;
        public int Writes;
        public int Count => _handlers.Count;
        public NetworkMessageDelegate this[ushort key]
        {
            get => _handlers[key];
            set
            {
                Writes++;
                _handlers[key] = value;
                if (FailNextPublication)
                {
                    FailNextPublication = false;
                    throw new IOException("test publication failure after mutation");
                }
            }
        }
        public bool TryGetValue(ushort key, out NetworkMessageDelegate handler) => _handlers.TryGetValue(key, out handler);
        public void Remove(ushort key) => _handlers.Remove(key);
    }
    public static class NetworkClient { public static HandlerDictionary handlers = new(); }
    public static class EncryptedChannelFunctions
    {
        public static int Reads;
        public static EncryptedChannelManager.EncryptedMessageOutside DeserializeEncryptedMessageOutside(NetworkReader reader)
        {
            Reads++;
            if (reader.ThrowOnRead) throw new IOException("test malformed message");
            return reader.Packed;
        }
    }
}
namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    {
        public static T ConvertDelegate<T>(Action<NetworkConnection, NetworkReader, int> handler) =>
            (T)(object)new NetworkMessageDelegate(handler);
    }
}
namespace Anomaly.Client
{
    public partial class Core
    {
        internal static void VerifyPatchForTests(string owner, MethodBase target, MethodInfo patch, HarmonyLib.HarmonyPatchType kind) =>
            RequireExactPatch(owner, new RequiredClientPatch(target, patch, kind));
    }
}
