using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal.Wrappers;
using HarmonyLib;
using Il2Cpp;
using Il2CppVoiceChat;
using Il2CppVoiceChat.Networking;

namespace Anomaly.Client.Patches.Voice;

[HarmonyPatch]
public static class VoiceTransceiverPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(VoiceTransceiver), nameof(VoiceTransceiver.ClientSendData))]
    public static void ClientSendDataPostfix(VoiceChatChannel targetChannel)
    {
        if (VoiceTransceiver._encodedBuffer == null || VoiceTransceiver._packageSize <= 0)
            return;

        ReferenceHub localHub = null;
        PlayerRegistry.TryGetLocalHub(out localHub);

        VoiceMessagePacket packet = VoiceMessagePacket.FromOutgoingData(localHub, targetChannel, VoiceTransceiver._encodedBuffer, VoiceTransceiver._packageSize);
        VoiceEvents.OnSent(new VoiceMessageSentEventArgs(packet));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(VoiceTransceiver), nameof(VoiceTransceiver.ClientReceiveMessage))]
    public static void ClientReceiveMessagePrefix(VoiceMessage msg)
    {
        VoiceMessagePacket packet = VoiceMessagePacket.FromMessage(msg, isOutgoing: false);
        VoiceEvents.OnReceiving(new VoiceMessageReceivingEventArgs(packet));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VoiceTransceiver), nameof(VoiceTransceiver.ClientReceiveMessage))]
    public static void ClientReceiveMessagePostfix(VoiceMessage msg)
    {
        VoiceMessagePacket packet = VoiceMessagePacket.FromMessage(msg, isOutgoing: false);
        VoiceEvents.OnReceived(new VoiceMessageReceivedEventArgs(packet));
    }
}
