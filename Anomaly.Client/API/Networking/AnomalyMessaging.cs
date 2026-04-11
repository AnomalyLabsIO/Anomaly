using Anomaly.Client.API.Features;
using Anomaly.Client.Events;
using Anomaly.Client.Events.Handlers;
using Anomaly.Client.Internal.Wrappers;
using Il2Cpp;
using Il2CppMirror;
using MelonLoader;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Anomaly.Client.API.Networking;

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

        // Serialize: [4 bytes hash][message payload]
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(AnomalyMessageRegistry.GetHash(msg.MessageName));
        msg.Serialize(writer);
        writer.Flush();
        var payload = ms.ToArray();

        // Build encrypted data: [0xFE channel][payload]
        var data = new byte[1 + payload.Length];
        data[0] = AnomalyChannel.ChannelId;
        Array.Copy(payload, 0, data, 1, payload.Length);

        // Encrypt with AES-GCM
        EncryptedChannelManager.EncryptedMessageOutside packed;
        if (NetworkServer.active)
        {
            // Local host -- no encryption needed
            packed = new EncryptedChannelManager.EncryptedMessageOutside(
                EncryptedChannelManager.SecurityLevel.Unsecured, data);
        }
        else
        {
            var encrypted = AesGcmEncrypt(data, ecm.EncryptionKey);
            packed = new EncryptedChannelManager.EncryptedMessageOutside(
                EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated, encrypted);
        }

        NetworkClient.Send(packed, msg.TransportChannel);
    }

    internal static void OnReceived(byte[] anomalyPayload)
    {
        if (anomalyPayload.Length < 4)
        {
            MelonLogger.Warning("AnomalyMessaging.OnReceived: Payload too short for message hash.");
            return;
        }

        uint hash = BitConverter.ToUInt32(anomalyPayload, 0);

        if (!AnomalyMessageRegistry.TryCreate(hash, out var message))
        {
            MelonLogger.Warning($"AnomalyMessaging.OnReceived: Unknown message hash 0x{hash:X8}.");
            return;
        }

        try
        {
            using var ms = new MemoryStream(anomalyPayload, 4, anomalyPayload.Length - 4);
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
