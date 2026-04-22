using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using HarmonyLib;
using Il2CppMirror;
using Il2CppVoiceChat.Networking;

namespace Anomaly.Client.Patches.Voice;

[HarmonyPatch(typeof(VoiceMessageReadersWriters), nameof(VoiceMessageReadersWriters.SerializeVoiceMessage))]
public static class VoiceMessageReadersWritersPatch
{
    [HarmonyPrefix]
    public static void SerializeVoiceMessagePrefix(NetworkWriter writer, VoiceMessage msg)
    {
        VoiceMessagePacket packet = VoiceMessagePacket.FromMessage(msg, isOutgoing: true);
        VoiceEvents.OnSending(new VoiceMessageSendingEventArgs(packet));
    }
}
