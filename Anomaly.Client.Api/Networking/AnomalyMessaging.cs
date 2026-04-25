using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal.Wrappers;
using Anomaly.Shared.Networking;
using Il2Cpp;
using Il2CppMirror;
using MelonLoader;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Anomaly.Client.Api.Networking;

public static class AnomalyMessaging
{
    public static event Action<IAnomalyMessage> MessageReceived;

    private static readonly SecureRandom SecureRandom = new();

    public static void Send(IAnomalyMessage msg)
    {
        var hub = LocalPlayer.Instance?.ReferenceHub;
        if (hub == null && !PlayerRegistry.TryGetLocalHub(out hub))
        {
            MelonLogger.Warning("AnomalyMessaging.Send: No local player hub available.");
            return;
        }

        var ecm = hub.encryptedChannelManager;
        if (ecm.EncryptionKey == null)
        {
            MelonLogger.Warning("AnomalyMessaging.Send: No encryption key available.");
            return;
        }

        uint counter = AnomalyReplayTable.Instance.NextTxCounter(msg.TransportChannel);
        uint hash = AnomalyMessageRegistry.GetHash(msg.MessageName);

        // Serialize the inner payload: [hash][message payload]
        using var innerMs = new MemoryStream();
        using var innerWriter = new BinaryWriter(innerMs);
        innerWriter.Write(hash);
        msg.Serialize(innerWriter);
        innerWriter.Flush();
        var inner = innerMs.ToArray();

        // Build the full envelope: [0xFE][4:counter][inner]
        var data = new byte[5 + inner.Length];
        data[0] = AnomalyChannel.ChannelId;
        BitConverter.GetBytes(counter).CopyTo(data, 1);
        Array.Copy(inner, 0, data, 5, inner.Length);

        EncryptedChannelManager.EncryptedMessageOutside packed;
        if (NetworkServer.active)
        {
            packed = new EncryptedChannelManager.EncryptedMessageOutside(
                EncryptedChannelManager.SecurityLevel.Unsecured, data);
        }
        else
        {
            var encrypted = AesGcmEncrypt(data, ecm.EncryptionKey);
            packed = new EncryptedChannelManager.EncryptedMessageOutside(
                EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated, encrypted);
        }

        NetworkClient.Send(packed, (int)msg.TransportChannel);
    }

    internal static void OnReceived(byte[] anomalyPayload)
    {
        // Wire (after stripping 0xFE): [4:counter][4:hash][N:payload]
        if (anomalyPayload.Length < 8)
        {
            MelonLogger.Warning("AnomalyMessaging.OnReceived: Payload too short for counter + message hash.");
            return;
        }

        uint counter = BitConverter.ToUInt32(anomalyPayload, 0);
        uint hash = BitConverter.ToUInt32(anomalyPayload, 4);

        if (!AnomalyMessageRegistry.TryGetChannel(hash, out var channel))
        {
            MelonLogger.Warning($"AnomalyMessaging.OnReceived: Unknown message hash 0x{hash:X8}.");
            return;
        }

        var rx = AnomalyReplayTable.Instance.TryAcceptRx(channel, counter, out bool shouldLog);
        if (rx != AnomalyReplayState.RxResult.Accepted)
        {
            if (shouldLog)
                MelonLogger.Warning($"AnomalyMessaging.OnReceived: Dropping packet (hash=0x{hash:X8}, counter={counter}, channel={channel}, reason={rx}).");
            return;
        }

        if (!AnomalyMessageRegistry.TryCreate(hash, out var message))
        {
            MelonLogger.Warning($"AnomalyMessaging.OnReceived: No factory for hash 0x{hash:X8}.");
            return;
        }

        try
        {
            using var ms = new MemoryStream(anomalyPayload, 8, anomalyPayload.Length - 8);
            using var reader = new BinaryReader(ms);
            message.Deserialize(reader);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"AnomalyMessaging.OnReceived: Failed to deserialize '{message.MessageName}': {ex}");
            return;
        }

        try
        {
            MessageReceived?.Invoke(message);
            NetworkMessageEvents.OnMessageReceived(message);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"AnomalyMessaging.OnReceived: Handler exception for '{message.MessageName}': {ex}");
        }
    }

    private static byte[] AesGcmEncrypt(byte[] data, byte[] key)
    {
        byte[] nonce = new byte[32];
        SecureRandom.NextBytes(nonce, 0, nonce.Length);

        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(true, new AeadParameters(new KeyParameter(key), 128, nonce));

        int outputSize = cipher.GetOutputSize(data.Length);
        var ciphertext = new byte[outputSize];
        int len = cipher.ProcessBytes(data, 0, data.Length, ciphertext, 0);
        cipher.DoFinal(ciphertext, len);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(nonce);
        writer.Write(ciphertext, 0, outputSize);
        return ms.ToArray();
    }
}
