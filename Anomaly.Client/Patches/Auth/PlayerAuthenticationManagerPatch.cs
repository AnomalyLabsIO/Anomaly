using System.Collections.Generic;
using System.Linq;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Auth;
using Anomaly.Client.Components;
using Anomaly.Client.Api.Internal.Wrappers;
using Anomaly.Shared.Networking;
using HarmonyLib;
using Il2Cpp;
using Il2CppCentralAuth;
using Il2CppCryptography;
using Il2CppGameCore;
using Il2CppRemoteAdmin;
using Il2CppSystem.Text;
using MelonLoader;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using UnityEngine;

namespace Anomaly.Client.Patches.Auth;

[HarmonyPatch]
public static class PlayerAuthenticationManagerPatch
{
    private struct PendingEncryptedMessage
    {
        public EncryptedChannelManager.EncryptedMessage Message;
        public EncryptedChannelManager.SecurityLevel SecurityLevel;
    }

    private static readonly Dictionary<EncryptedChannelManager.EncryptedChannel, List<PendingEncryptedMessage>> PendingClientMessages = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerAuthenticationManager), nameof(PlayerAuthenticationManager.Start))]
    public static void Start(PlayerAuthenticationManager __instance)
    {
        if (!__instance.isLocalPlayer) return;
        if (!AuthHandler.UsingAnomalyAuth) return;
        AuthStatusOverlay.EndAuthenticating();
        ConnectionInfoOverlay.EndOverride();
        __instance._hub.encryptedChannelManager.EncryptionKey = AuthHandler.CurrentKey;
        MelonLogger.Warning("Starting player authentication manager");
        FlushPendingClientMessages();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameConsoleTransmission), nameof(GameConsoleTransmission.Start))]
    public static void GameConsoleTransmissionStart(GameConsoleTransmission __instance)
    {
        if (!__instance.isLocalPlayer) return;
        MelonLogger.Msg("GameConsoleTransmission.Start reached for local player; flushing queued GameConsole messages if any.");
        FlushPendingClientMessages(EncryptedChannelManager.EncryptedChannel.GameConsole);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(QueryProcessor), nameof(QueryProcessor.Start))]
    public static void QueryProcessorStart(QueryProcessor __instance)
    {
        if (!__instance.isLocalPlayer) return;
        MelonLogger.Msg("QueryProcessor.Start reached for local player; flushing queued RA/AdminChat messages if any.");
        FlushPendingClientMessages(EncryptedChannelManager.EncryptedChannel.RemoteAdmin);
        FlushPendingClientMessages(EncryptedChannelManager.EncryptedChannel.AdminChat);
    }

    private static int _lastSendFrame = -1;
    private static long _lastSendFingerprint;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EncryptedChannelManager), nameof(EncryptedChannelManager.TrySendMessageToServer), new Type[] { typeof(string), typeof(EncryptedChannelManager.EncryptedChannel) })]
    public static bool OnPack(EncryptedChannelManager __instance, out bool __result,
        string content, EncryptedChannelManager.EncryptedChannel channel)
    {
        __result = false;
        if (!AuthHandler.UsingAnomalyAuth)
        {
            return true;
        }

        // Same IL2CPP double-dispatch shape as the receive path: the prefix
        // fires 2–3 times per underlying send, and a naive frame-only dedup
        // drops the second legitimate send that shares a frame with another.
        // Fingerprint content + channel so repeat dispatches of the same
        // send are squashed while two distinct sends in the same frame both
        // go through.
        int frame = UnityEngine.Time.frameCount;
        long fp = ComputeSendFingerprint(content, channel);
        if (frame == _lastSendFrame && fp == _lastSendFingerprint)
        {
            __result = true; // first call already handled it
            return false;
        }
        _lastSendFrame = frame;
        _lastSendFingerprint = fp;

        if (__instance._txCounter == 4294967295U)
            __instance._txCounter = 0;
        __instance._txCounter++;

        // `data` is a managed byte[]. `using Il2CppSystem.Text;` makes the bare
        // `Encoding.UTF8` resolve to the IL2CPP wrapper, which marshals managed
        // byte[] arguments to a temporary IL2CPP copy — the original array is
        // never written. Result: ciphertext encrypts an all-zero buffer and the
        // server receives content that decodes to NUL chars. Use the managed
        // System.Text.Encoding explicitly so the bytes actually land in `data`.
        var data = new byte[Misc.Utf8Encoding.GetByteCount(content) + 5];
        data[0] = (byte)channel;
        BitConverter.GetBytes(__instance._txCounter).CopyTo(data, 1);
        System.Text.Encoding.UTF8.GetBytes(content, 0, content.Length, data, 5);

        // Same fallback as receive: hub's EncryptionKey isn't populated
        // until PlayerAuthenticationManager.Start fires, but sends can be
        // triggered earlier (early chat / admin commands). Use AuthHandler's
        // pre-computed shared secret if the hub doesn't have it yet.
        byte[] key = __instance.EncryptionKey;
        if (key == null)
        {
            key = AuthHandler.CurrentKey;
            if (key == null)
            {
                MelonLogger.Warning("Tried to send encrypted message, but no key was available (hub and AuthHandler both null)");
                __result = false;
                return false;
            }
            __instance.EncryptionKey = key;
        }

        var encryptedData = AesGcmEncrypt(data, key, _secureRandom);
        MelonLogger.Warning("Sending encrypted message outside");
        EncryptedMessageTransport.Send(
            EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated,
            encryptedData,
            0);
        __result = true;
        return false;
    }

    private static readonly SecureRandom _secureRandom = new();

    private static byte[] AesGcmEncrypt(byte[] data, byte[] secret, SecureRandom secureRandom)
    {
        byte[] array = new byte[32];
        byte[] result;

        secureRandom.NextBytes(array, 0, array.Length);
        GcmBlockCipher gcmBlockCipher = new GcmBlockCipher(new AesEngine());
        gcmBlockCipher.Init(true, new AeadParameters(new KeyParameter(secret), 128, array));
        int outputSize = gcmBlockCipher.GetOutputSize(data.Length);
        var array2 = new byte[outputSize];
        int outOff = gcmBlockCipher.ProcessBytes(data, 0, data.Length, array2, 0);
        gcmBlockCipher.DoFinal(array2, outOff);
        using (MemoryStream memoryStream = new MemoryStream())
        {
            using (BinaryWriter binaryWriter = new BinaryWriter(memoryStream))
            {
                binaryWriter.Write(array);
                binaryWriter.Write(array2, 0, outputSize);
            }
            result = memoryStream.ToArray();
        }
        return result;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EncryptedChannelManager), nameof(EncryptedChannelManager.ClientReceivePackedMessage))]
    public static bool OnReceivePackedMessage(EncryptedChannelManager.EncryptedMessageOutside packed)
    {
        if (!AuthHandler.UsingAnomalyAuth)
            return true;

        // IL2CPP re-marshals the byte[] field per Harmony prefix dispatch,
        // so the same underlying receive fires this prefix 2–3 times with a
        // fresh byte[] of identical content each call. Both vanilla and
        // Anomaly branches below gate on a strict-monotonic counter
        // (vanilla: EncryptedChannelManager._rxCounter; Anomaly: per-channel
        // state on AnomalyReplayTable), which naturally rejects the duplicate
        // dispatches as replays.

        MelonLogger.Warning("Got message back.");

        ReferenceHub hub = LocalPlayer.Instance?.ReferenceHub;
        if (hub == null && !PlayerRegistry.TryGetLocalHub(out hub))
        {
            MelonLogger.Warning("Got encrypted message before the local player hub was ready.");
            return false;
        }

        // The hub's EncryptionKey is set in PlayerAuthenticationManager.Start
        // (our postfix), which fires when the local player's hub spawns. But
        // pre-registered EncryptedMessageOutside messages can arrive BEFORE
        // that — our own AuthHandler already knows the shared secret from
        // phase-2 auth, so fall back to AuthHandler.CurrentKey. Sync it onto
        // the hub once we have both so subsequent vanilla code paths work.
        byte[] key = hub.encryptedChannelManager.EncryptionKey;
        if (key == null)
        {
            key = AuthHandler.CurrentKey;
            if (key == null)
            {
                MelonLogger.Warning("Got EncryptedMessage but no encryption key available (hub and AuthHandler both null)");
                return false;
            }
            hub.encryptedChannelManager.EncryptionKey = key;
        }
        var decryptedData = AES.AesGcmDecrypt(packed.Data, key);

        var channel = (EncryptedChannelManager.EncryptedChannel)decryptedData[0];

        // Anomaly custom messages -- separate dispatch. Wire after stripping
        // the 0xFE byte is [counter][hash][payload]; AnomalyMessaging.OnReceived
        // parses the counter/hash and applies per-channel replay protection.
        if ((byte)channel == AnomalyChannel.ChannelId)
        {
            var anomalyPayload = new byte[decryptedData.Length - 1];
            Array.Copy(decryptedData, 1, anomalyPayload, 0, anomalyPayload.Length);
            AnomalyMessaging.OnReceived(anomalyPayload);
            return false;
        }

        var counter = BitConverter.ToUInt32(decryptedData, 1);
        var content = Encoding.UTF8.GetString(decryptedData, 5, decryptedData.Length - 5);
        EncryptedChannelManager.EncryptedMessage message = new EncryptedChannelManager.EncryptedMessage(channel, content, counter);

        if (hub.encryptedChannelManager._rxCounter == 4294967295U)
            hub.encryptedChannelManager._rxCounter = 0;

        if(counter <= hub.encryptedChannelManager._rxCounter)
        {
            MelonLogger.Warning(string.Format("Received message with counter {0}, which is lower or equal to last received message counter {1}. Discarding message!", counter, hub.encryptedChannelManager._rxCounter));
            return false;
        }

        hub.encryptedChannelManager._rxCounter = counter;


        if (!EncryptedChannelManager.ClientChannelHandlers.ContainsKey(channel))
        {
            QueuePendingClientMessage(channel, message, packed.Level);
            return false;
        }

        try
        {
            FlushPendingClientMessages(channel);
            EncryptedChannelManager.ClientChannelHandlers[channel].Invoke(message, packed.Level);
        }
        catch(Exception ex)
        {
            Il2CppGameCore.Console.AddLog(string.Format("Exception while handling encrypted message on channel {0} (client, running a handler). Exception: {1}", channel, ex.Message), Color.red, false, ConsoleLogType.Log);
            Il2CppGameCore.Console.AddLog(ex.StackTrace, Color.red, false, ConsoleLogType.Log);
        }
        return false;
    }

    private static long ComputeSendFingerprint(string content, EncryptedChannelManager.EncryptedChannel channel)
    {
        long fp = (long)channel;
        if (content == null) return fp;
        int len = content.Length;
        fp = unchecked(fp * 1315423911L + len);
        int headEnd = len < 4 ? len : 4;
        for (int i = 0; i < headEnd; i++)
            fp = unchecked(fp * 31L + content[i]);
        int tailStart = len < 8 ? headEnd : len - 4;
        for (int i = tailStart; i < len; i++)
            fp = unchecked(fp * 31L + content[i]);
        return fp;
    }

    private static void QueuePendingClientMessage(
        EncryptedChannelManager.EncryptedChannel channel,
        EncryptedChannelManager.EncryptedMessage message,
        EncryptedChannelManager.SecurityLevel securityLevel)
    {
        if (!PendingClientMessages.TryGetValue(channel, out var pending))
        {
            pending = new List<PendingEncryptedMessage>();
            PendingClientMessages[channel] = pending;
        }

        pending.Add(new PendingEncryptedMessage
        {
            Message = message,
            SecurityLevel = securityLevel
        });

        MelonLogger.Warning($"Queued encrypted channel {channel} until its client handler is ready.");
    }

    private static void FlushPendingClientMessages()
    {
        if (PendingClientMessages.Count == 0) return;

        foreach (var channel in PendingClientMessages.Keys.ToArray())
            FlushPendingClientMessages(channel);
    }

    private static void FlushPendingClientMessages(EncryptedChannelManager.EncryptedChannel channel)
    {
        if (!PendingClientMessages.TryGetValue(channel, out var pending) || pending.Count == 0)
            return;

        if (!EncryptedChannelManager.ClientChannelHandlers.ContainsKey(channel))
            return;

        var handler = EncryptedChannelManager.ClientChannelHandlers[channel];
        MelonLogger.Msg($"Flushing {pending.Count} queued encrypted messages for client channel {channel}.");
        foreach (var pendingMessage in pending)
            handler.Invoke(pendingMessage.Message, pendingMessage.SecurityLevel);

        pending.Clear();
        PendingClientMessages.Remove(channel);
    }
}
