using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Wrappers;
using HarmonyLib;
using Il2Cpp;
using Il2CppVoiceChat;
using Il2CppVoiceChat.Networking;

namespace Anomaly.Client.Patches.Voice;

/// <summary>
/// Voice send/receive event dispatch. Both <c>Sending</c> and <c>Receiving</c>
/// are now cancellable — subscribers can suppress outgoing voice frames or
/// incoming voice playback on a per-message basis.
///
/// <c>Sending</c> fires from the <c>ClientSendData</c> prefix (was previously
/// in <c>VoiceMessageReadersWriters.SerializeVoiceMessage</c>; that location
/// would corrupt Mirror's writer if cancelled). <c>Receiving</c> fires from
/// <c>ClientReceiveMessage</c> prefix — cancelling skips local playback.
/// </summary>
[HarmonyPatch]
public static class VoiceTransceiverPatch
{
    // Bridges the prefix's cancellation decision to the existing Sent/Received
    // postfix so cancelled frames don't fire the post-event. Single-threaded
    // Unity main-thread context; no [ThreadStatic] needed.
    private static bool _sendCancelled;
    private static bool _receiveCancelled;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(VoiceTransceiver), nameof(VoiceTransceiver.ClientSendData))]
    public static bool ClientSendDataPrefix(VoiceChatChannel targetChannel)
    {
        _sendCancelled = false;
        if (VoiceTransceiver._encodedBuffer == null || VoiceTransceiver._packageSize <= 0)
            return true;

        PlayerRegistry.TryGetLocalHub(out ReferenceHub localHub);
        VoiceMessagePacket packet = VoiceMessagePacket.FromOutgoingData(localHub, targetChannel, VoiceTransceiver._encodedBuffer, VoiceTransceiver._packageSize);
        VoiceMessageSendingEventArgs ev = new(packet);
        VoiceEvents.OnSending(ev);
        if (!ev.IsAllowed)
        {
            _sendCancelled = true;
            return false;
        }
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VoiceTransceiver), nameof(VoiceTransceiver.ClientSendData))]
    public static void ClientSendDataPostfix(VoiceChatChannel targetChannel)
    {
        if (_sendCancelled)
        {
            _sendCancelled = false;
            return;
        }
        if (VoiceTransceiver._encodedBuffer == null || VoiceTransceiver._packageSize <= 0)
            return;

        PlayerRegistry.TryGetLocalHub(out ReferenceHub localHub);
        VoiceMessagePacket packet = VoiceMessagePacket.FromOutgoingData(localHub, targetChannel, VoiceTransceiver._encodedBuffer, VoiceTransceiver._packageSize);
        VoiceEvents.OnSent(new VoiceMessageSentEventArgs(packet));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(VoiceTransceiver), nameof(VoiceTransceiver.ClientReceiveMessage))]
    public static bool ClientReceiveMessagePrefix(VoiceMessage msg)
    {
        _receiveCancelled = false;
        VoiceMessagePacket packet = VoiceMessagePacket.FromMessage(msg, isOutgoing: false);
        VoiceMessageReceivingEventArgs ev = new(packet);
        VoiceEvents.OnReceiving(ev);
        if (!ev.IsAllowed)
        {
            _receiveCancelled = true;
            return false;
        }
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VoiceTransceiver), nameof(VoiceTransceiver.ClientReceiveMessage))]
    public static void ClientReceiveMessagePostfix(VoiceMessage msg)
    {
        if (_receiveCancelled)
        {
            _receiveCancelled = false;
            return;
        }
        VoiceMessagePacket packet = VoiceMessagePacket.FromMessage(msg, isOutgoing: false);
        VoiceEvents.OnReceived(new VoiceMessageReceivedEventArgs(packet));
    }
}
