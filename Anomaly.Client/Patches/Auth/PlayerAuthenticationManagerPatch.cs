using System.Collections.Generic;
using System.Linq;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Auth;
using Anomaly.Client.Components;
using Anomaly.Client.Api.Internal.Wrappers;
using Anomaly.Client.Api.Modding;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using HarmonyLib;
using Il2Cpp;
using Il2CppCentralAuth;
using Il2CppGameCore;
using Il2CppMirror;
using Il2CppRemoteAdmin;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Patches.Auth;

[HarmonyPatch]
public static class PlayerAuthenticationManagerPatch
{
    private const int EncryptedMessageHeaderSize = 5;

    private struct PendingEncryptedMessage
    {
        public EncryptedChannelManager.EncryptedMessage Message;
        public EncryptedChannelManager.SecurityLevel SecurityLevel;
        public int ByteCount;
    }

    private const int MaxPendingClientMessagesPerChannel = 16;
    private const int MaxPendingClientMessages = 64;
    private const int MaxPendingClientBytes = 2 * 1024 * 1024;
    private const uint GenericTransportRateKeyPrefix = 0xFFF00000u;
    private const uint RawTransportRateKey = 0xFFE00000u;
    private static readonly Dictionary<EncryptedChannelManager.EncryptedChannel, List<PendingEncryptedMessage>> PendingClientMessages = new();
    private static readonly InboundEnvelopeDispatchCoalescer ReceiveDispatchCoalescer = new();
    private static int _pendingClientMessageCount;
    private static int _pendingClientBytes;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerAuthenticationManager), nameof(PlayerAuthenticationManager.Start))]
    public static void Start(PlayerAuthenticationManager __instance)
    {
        if (!__instance.isLocalPlayer) return;
        if (!AuthHandler.UsingAnomalyAuth) return;
        AuthStatusOverlay.EndAuthenticating();
        ConnectionInfoOverlay.EndOverride();
        // Anomaly's directional authenticated transport never places a
        // bidirectional session key on the game's public EncryptedChannelManager
        // object. Our prefixes below encrypt each direction with its dedicated key.
        __instance._hub.encryptedChannelManager.EncryptionKey = null;
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

        if (!TryAdvanceTransportCounter(__instance))
            return false;

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

        return SendPackedClientData(data, out __result);
    }

    internal static bool SendPackedClientData(byte[] data, out bool result)
    {
        if (NetworkServer.active)
        {
            result = EncryptedMessageTransport.Send(
                EncryptedChannelManager.SecurityLevel.Unsecured,
                data,
                0);
            return false;
        }

        try
        {
            if (!AnomalyMessaging.TryEncryptClientEnvelope(data, out var encryptedData))
            {
                AnomalyMessaging.FailClosedTransport(
                    "The client-to-server encrypted send path lost its directional authenticated transport session.");
                SafeLogWarning(
                    "Tried to send encrypted message without a directional authenticated transport session.");
                result = false;
                return false;
            }

            result = EncryptedMessageTransport.Send(
                EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated,
                encryptedData,
                0);
            if (!result)
            {
                AnomalyMessaging.FailClosedTransport(
                    "The required client-to-server encrypted delivery path rejected the packet.");
                SafeLogWarning("Encrypted client-to-server delivery returned false.");
            }
            return false;
        }
        catch (Exception ex)
        {
            AnomalyMessaging.FailClosedTransport(
                "The required client-to-server encrypted send path failed.");
            SafeLogWarning(
                $"Failed to encrypt or deliver client-to-server message: {ex.GetType().Name}: {ex.Message}");
            result = false;
            return false;
        }
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

        byte[] packedData = packed.Data;
        var rawLimiter = AnomalyReplayTable.RawInboundRateLimiter;
        var dispatchDisposition = ReceiveDispatchCoalescer.Observe(
            UnityEngine.Time.frameCount,
            (byte)packed.Level,
            packedData);
        if (dispatchDisposition == InboundEnvelopeDispatchCoalescer.DispatchDisposition.IgnoreExpectedDuplicate)
            return false;
        if (dispatchDisposition == InboundEnvelopeDispatchCoalescer.DispatchDisposition.ChargeExcessDuplicate)
        {
            HandleRawTransportResult(
                rawLimiter.RegisterProtocolViolation(),
                "Encrypted-channel receive exceeded the expected duplicate Harmony invocation count.");
            return false;
        }

        if (packedData == null)
        {
            HandleRawTransportResult(
                rawLimiter.RegisterProtocolViolation(),
                "Encrypted-channel payload was null.");
            return false;
        }
        if (packedData.Length == 0)
        {
            HandleRawTransportResult(
                rawLimiter.RegisterProtocolViolation(),
                "Encrypted-channel payload was empty.");
            return false;
        }
        if (packedData.Length > AnomalyChannel.MaxEncryptedEnvelopeBytes)
        {
            HandleRawTransportResult(
                rawLimiter.RegisterProtocolViolation(),
                $"Encrypted-channel payload exceeded the absolute ceiling " +
                $"({packedData.Length}/{AnomalyChannel.MaxEncryptedEnvelopeBytes} bytes).");
            return false;
        }

        var rawRateResult = rawLimiter.TryAccept(
            RawTransportRateKey,
            Math.Min(packedData.Length, AnomalyChannel.MaxEnvelopeBytes));
        if (!rawRateResult.Allowed)
        {
            HandleRawTransportResult(
                rawRateResult,
                $"Pre-decrypt traffic exceeded bounded packet/byte limits ({packedData.Length} bytes).");
            return false;
        }

        ReferenceHub hub = LocalPlayer.Instance?.ReferenceHub;
        if (hub == null && !PlayerRegistry.TryGetLocalHub(out hub))
        {
            HandleRawTransportResult(
                rawLimiter.RegisterProtocolViolation(),
                "Encrypted message arrived before the local player hub was ready.");
            return false;
        }

        byte[] decryptedData;
        if (packed.Level == EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated)
        {
            if (!AuthTransportCrypto.IsVersion2Envelope(packedData))
            {
                HandleRawTransportResult(
                    rawLimiter.RegisterProtocolViolation(),
                    "Rejected a legacy or malformed directional AEAD v2 envelope for application protocol v3.");
                return false;
            }

            try
            {
                if (!AnomalyMessaging.TryDecryptServerEnvelope(packedData, out decryptedData))
                {
                    HandleRawTransportResult(
                        rawLimiter.RegisterProtocolViolation(),
                        "Encrypted message had no directional authenticated transport session.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                HandleRawTransportResult(
                    rawLimiter.RegisterProtocolViolation(),
                    $"Encrypted message authentication failed ({packedData.Length} bytes, error={ex.GetType().Name}).");
                return false;
            }
        }
        else if (packed.Level == EncryptedChannelManager.SecurityLevel.Unsecured)
        {
            if (!NetworkServer.active)
            {
                HandleRawTransportResult(
                    rawLimiter.RegisterProtocolViolation(),
                    "Rejected an unsecured encrypted-channel message from a remote server.");
                return false;
            }

            decryptedData = packedData;
        }
        else
        {
            HandleRawTransportResult(
                rawLimiter.RegisterProtocolViolation(),
                $"Rejected an encrypted-channel message with unknown security level {packed.Level}.");
            return false;
        }

        if (decryptedData == null || decryptedData.Length < EncryptedMessageHeaderSize)
        {
            HandleRawTransportResult(
                rawLimiter.RegisterProtocolViolation(),
                $"Decoded encrypted-channel message was too short ({decryptedData?.Length ?? 0} bytes).");
            return false;
        }

        var channel = (EncryptedChannelManager.EncryptedChannel)decryptedData[0];

        // Anomaly custom messages -- separate dispatch. Wire after stripping
        // the 0xFE byte is [counter][hash][payload]; AnomalyMessaging.OnReceived
        // parses the counter/hash and applies per-channel replay protection.
        if ((byte)channel == AnomalyChannel.ChannelId)
        {
            var anomalyPayload = new byte[decryptedData.Length - 1];
            Array.Copy(decryptedData, 1, anomalyPayload, 0, anomalyPayload.Length);
            AnomalyMessaging.OnReceived(
                anomalyPayload,
                packed.Level == EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated,
                NetworkServer.active);
            return false;
        }

        if (!NetworkServer.active)
        {
            int payloadBytes = decryptedData.Length - EncryptedMessageHeaderSize;
            uint rateKey = GenericTransportRateKeyPrefix | (byte)channel;
            var rateResult = AnomalyReplayTable.InboundRateLimiter.TryAccept(rateKey, payloadBytes);
            if (!rateResult.Allowed)
            {
                if (rateResult.ShouldDisconnect)
                {
                    AnomalyMessaging.FailClosedTransport(
                        "Sustained generic encrypted-channel traffic exceeded bounded limits.");
                }
                if (rateResult.ShouldLog)
                {
                    SafeLogWarning(
                        $"Rate-limited generic encrypted channel {channel} " +
                        $"({payloadBytes} bytes, drops={rateResult.DroppedInWindow}).");
                }
                return false;
            }
        }

        var counter = BitConverter.ToUInt32(decryptedData, 1);
        var content = new byte[decryptedData.Length - EncryptedMessageHeaderSize];
        Array.Copy(decryptedData, EncryptedMessageHeaderSize, content, 0, content.Length);
        EncryptedChannelManager.EncryptedMessage message = new EncryptedChannelManager.EncryptedMessage(channel, content, counter);

        if (counter == 0U || counter == uint.MaxValue)
        {
            FailTransportCounter("Received an invalid or exhausted generic transport counter.");
            return false;
        }

        if(counter <= hub.encryptedChannelManager._rxCounter)
        {
            HandleRawTransportResult(
                rawLimiter.RegisterProtocolViolation(),
                $"Rejected encrypted counter {counter}; high-water mark is {hub.encryptedChannelManager._rxCounter}.");
            return false;
        }

        hub.encryptedChannelManager._rxCounter = counter;
        if (counter >= uint.MaxValue - 1U)
        {
            FailTransportCounter("Generic receive counter reached its final usable value.");
            return false;
        }


        if (!EncryptedChannelManager.ClientChannelHandlers.ContainsKey(channel))
        {
            QueuePendingClientMessage(channel, message, packed.Level, content.Length);
            return false;
        }

        FlushPendingClientMessages(channel);
        InvokeClientChannelHandler(
            channel,
            EncryptedChannelManager.ClientChannelHandlers[channel],
            message,
            packed.Level);
        return false;
    }

    internal static bool TryAdvanceTransportCounter(EncryptedChannelManager manager)
    {
        if (manager == null || manager._txCounter >= uint.MaxValue - 1U)
        {
            FailTransportCounter("Authenticated transport counter exhausted before wrap.");
            return false;
        }

        manager._txCounter++;
        return true;
    }

    private static void FailTransportCounter(string reason)
    {
        AnomalyMessaging.FailClosedTransport(reason);
    }

    private static void HandleRawTransportResult(AnomalyRateLimitResult result, string message)
    {
        if (result.ShouldDisconnect && !NetworkServer.active)
        {
            AnomalyMessaging.FailClosedTransport(
                "Sustained invalid or excessive pre-decrypt traffic was rejected.");
        }
        if (result.ShouldLog)
        {
            SafeLogWarning($"{message} Drops={result.DroppedInWindow}.");
        }
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
        EncryptedChannelManager.SecurityLevel securityLevel,
        int byteCount)
    {
        if (byteCount < 0 || byteCount > MaxPendingClientBytes)
        {
            RegisterPendingQueueOverflow(channel, byteCount);
            return;
        }

        if (!PendingClientMessages.TryGetValue(channel, out var pending))
        {
            pending = new List<PendingEncryptedMessage>();
            PendingClientMessages[channel] = pending;
        }

        if (pending.Count >= MaxPendingClientMessagesPerChannel ||
            _pendingClientMessageCount >= MaxPendingClientMessages ||
            byteCount > MaxPendingClientBytes - _pendingClientBytes)
        {
            RegisterPendingQueueOverflow(channel, byteCount);
            return;
        }

        pending.Add(new PendingEncryptedMessage
        {
            Message = message,
            SecurityLevel = securityLevel,
            ByteCount = byteCount
        });
        _pendingClientMessageCount++;
        _pendingClientBytes += byteCount;

        MelonLogger.Warning(
            $"Queued encrypted channel {channel} until its client handler is ready " +
            $"({_pendingClientMessageCount}/{MaxPendingClientMessages} messages, " +
            $"{_pendingClientBytes}/{MaxPendingClientBytes} bytes).");
    }

    private static void RegisterPendingQueueOverflow(
        EncryptedChannelManager.EncryptedChannel channel,
        int byteCount)
    {
        var violation = AnomalyReplayTable.InboundRateLimiter.RegisterProtocolViolation();
        if (violation.ShouldDisconnect && !NetworkServer.active)
        {
            AnomalyMessaging.FailClosedTransport(
                "Sustained encrypted-channel pending-queue overflow was rejected.");
        }
        if (violation.ShouldLog)
        {
            SafeLogWarning(
                $"Dropped encrypted channel {channel} while its handler was unavailable because the bounded " +
                $"pending queue was full (messageBytes={byteCount}, drops={violation.DroppedInWindow}).");
        }
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
        int pendingCount = pending.Count;
        PendingClientMessages.Remove(channel);
        _pendingClientMessageCount = Math.Max(0, _pendingClientMessageCount - pending.Count);
        foreach (var pendingMessage in pending)
            _pendingClientBytes = Math.Max(0, _pendingClientBytes - pendingMessage.ByteCount);

        foreach (var pendingMessage in pending)
        {
            InvokeClientChannelHandler(
                channel,
                handler,
                pendingMessage.Message,
                pendingMessage.SecurityLevel);
        }

        pending.Clear();
        try { MelonLogger.Msg($"Flushed {pendingCount} queued encrypted messages for client channel {channel}."); }
        catch { }
    }

    private static void InvokeClientChannelHandler(
        EncryptedChannelManager.EncryptedChannel channel,
        EncryptedChannelManager.EncryptedMessageClientHandler handler,
        EncryptedChannelManager.EncryptedMessage message,
        EncryptedChannelManager.SecurityLevel securityLevel)
    {
        foreach (Il2CppSystem.Delegate subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EncryptedChannelManager.EncryptedMessageClientHandler)subscriber)
                    .Invoke(message, securityLevel);
            }
            catch (Exception exception)
            {
                try
                {
                    MelonLogger.Error(
                        $"Encrypted client channel {channel} handler threw: {exception}");
                }
                catch { }
            }
        }
    }

    internal static void ResetPendingClientMessages()
    {
        PendingClientMessages.Clear();
        _pendingClientMessageCount = 0;
        _pendingClientBytes = 0;
        ReceiveDispatchCoalescer.Reset();
        _lastSendFrame = -1;
        _lastSendFingerprint = 0;
    }

    private static void SafeLogWarning(string message)
    {
        try { MelonLogger.Warning(message); }
        catch { }
    }
}

/// <summary>
/// Optional compatibility patch for game versions that expose a managed byte[]
/// encrypted-channel send overload. Keeping this target in its own Harmony class
/// prevents an absent upstream overload from aborting the required string-send and
/// receive patches in <see cref="PlayerAuthenticationManagerPatch"/>.
/// </summary>
[HarmonyPatch]
internal static class OptionalByteArrayClientSendPatch
{
    private const string FeatureId = "encrypted-channel-byte-send";
    private const string ExpectedTarget =
        "EncryptedChannelManager.TrySendMessageToServer(byte[], EncryptedChannel)";
    private static int _lastSendFrame = -1;
    private static long _lastSendFingerprint;

    [HarmonyPrepare]
    public static bool Prepare()
    {
        var target = ResolveTarget();
        if (target != null)
        {
            FeatureHealth.Report(
                "anomaly",
                FeatureId,
                FeatureHealthStatus.Healthy,
                "Optional byte-array encrypted sends are available.",
                ExpectedTarget,
                target.DeclaringType?.FullName + "." + target.Name,
                string.Empty);
            return true;
        }

        const string detail =
            "Optional EncryptedChannelManager.TrySendMessageToServer(byte[], EncryptedChannel) " +
            "was not present; byte-array encrypted sends are disabled while string sends remain active.";
        FeatureHealth.Report(
            "anomaly",
            FeatureId,
            FeatureHealthStatus.Degraded,
            detail,
            ExpectedTarget,
            string.Empty,
            "Use the string encrypted-send API on this game build; update the hook only after verifying a new upstream signature.");
        MelonLogger.Warning("Harmony degraded: " + detail);
        return false;
    }

    [HarmonyTargetMethod]
    public static System.Reflection.MethodBase TargetMethod() => ResolveTarget();

    private static System.Reflection.MethodBase ResolveTarget() => AccessTools.Method(
        typeof(EncryptedChannelManager),
        nameof(EncryptedChannelManager.TrySendMessageToServer),
        new[] { typeof(byte[]), typeof(EncryptedChannelManager.EncryptedChannel) });

    [HarmonyPrefix]
    public static bool OnPackBytes(
        EncryptedChannelManager __instance,
        out bool __result,
        byte[] content,
        EncryptedChannelManager.EncryptedChannel channel)
    {
        __result = false;
        if (!AuthHandler.UsingAnomalyAuth)
            return true;
        if (content == null)
        {
            MelonLogger.Warning("Tried to send a null encrypted byte[] message.");
            return false;
        }

        int frame = UnityEngine.Time.frameCount;
        long fingerprint = ComputeSendFingerprint(content, channel);
        if (frame == _lastSendFrame && fingerprint == _lastSendFingerprint)
        {
            __result = true;
            return false;
        }
        _lastSendFrame = frame;
        _lastSendFingerprint = fingerprint;

        if (!PlayerAuthenticationManagerPatch.TryAdvanceTransportCounter(__instance))
            return false;

        var data = new byte[content.Length + 5];
        data[0] = (byte)channel;
        BitConverter.GetBytes(__instance._txCounter).CopyTo(data, 1);
        Array.Copy(content, 0, data, 5, content.Length);
        return PlayerAuthenticationManagerPatch.SendPackedClientData(data, out __result);
    }

    private static long ComputeSendFingerprint(
        byte[] content,
        EncryptedChannelManager.EncryptedChannel channel)
    {
        long fingerprint = (long)channel;
        if (content == null)
            return fingerprint;

        int length = content.Length;
        fingerprint = unchecked(fingerprint * 1315423911L + length);
        int headEnd = length < 4 ? length : 4;
        for (int i = 0; i < headEnd; i++)
            fingerprint = unchecked(fingerprint * 31L + content[i]);
        int tailStart = length < 8 ? headEnd : length - 4;
        for (int i = tailStart; i < length; i++)
            fingerprint = unchecked(fingerprint * 31L + content[i]);
        return fingerprint;
    }
}
