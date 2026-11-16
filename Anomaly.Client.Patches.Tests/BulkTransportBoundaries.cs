using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppLiteNetLib;

namespace Il2CppLiteNetLib
{
    public enum DeliveryMethod { Unreliable, ReliableUnordered, Sequenced, ReliableOrdered, ReliableSequenced }
    public sealed class NetPeer { }
    public sealed class NetManager
    {
        private static long _nextPointer;
        public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _nextPointer);
        public NetPeer FirstPeer;
        public byte ChannelsCount = 1;
    }
}

namespace Il2CppMirror
{
    public class Transport
    {
        private static long _nextPointer;
        public static Transport active;
        public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _nextPointer);
        public virtual int GetBatchThreshold(int channelId) => int.MaxValue;
    }
}

namespace Il2CppMirror.LiteNetLib4Mirror
{
    public sealed class LiteNetLib4MirrorTransport : Transport
    {
        public static LiteNetLib4MirrorTransport Singleton;
        public Il2CppStructArray<DeliveryMethod> channels;
    }

    public static class LiteNetLib4MirrorCore
    {
        public static NetManager Host;
        public static int OptionsCalls;
        public static void SetOptions(bool server)
        {
            OptionsCalls++;
            Host.ChannelsCount = checked((byte)LiteNetLib4MirrorTransport.Singleton.channels.Length);
        }
    }
}

namespace Anomaly.Client.FileTransfer
{
    public static class FileTransferClient
    {
        public static byte ProfileVersion;
        public static ulong LaneMask;
        internal static Func<bool> TransportAvailable;
        internal static void ConfigureBulkProfile(byte version, ulong ownedMask, Func<bool> transportAvailable = null)
        {
            ProfileVersion = version;
            LaneMask = ownedMask;
            TransportAvailable = transportAvailable;
        }
    }
}
