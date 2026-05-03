using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMirror;
using MelonLoader;

namespace Anomaly.Client.Api.Networking;

internal static class EncryptedMessageTransport
{
    // Mirror's stable message id for EncryptedChannelManager.EncryptedMessageOutside.
    // Keep this in sync with EncryptedChannelHandlerFix on the client receive path.
    private const ushort EncryptedMessageOutsideId = 9934;

    internal static bool Send(EncryptedChannelManager.SecurityLevel level, byte[] data, int channelId)
    {
        if (data == null)
        {
            MelonLogger.Warning($"EncryptedMessageTransport.Send: refusing to send null payload (level={level}, channelId={channelId}).");
            return false;
        }

        var connection = NetworkClient.connection;
        if (connection == null)
        {
            MelonLogger.Warning("EncryptedMessageTransport.Send: NetworkClient.connection is null.");
            return false;
        }

        var writer = NetworkWriterPool.Get();
        try
        {
            NetworkWriterExtensions.WriteUShort(writer, EncryptedMessageOutsideId);
            NetworkWriterExtensions.WriteByte(writer, (byte)level);
            NetworkWriterExtensions.WriteArray<byte>(writer, new Il2CppStructArray<byte>(data));

            connection.Send(writer.ToArraySegment(), channelId);
            return true;
        }
        finally
        {
            NetworkWriterPool.Return(writer);
        }
    }
}
