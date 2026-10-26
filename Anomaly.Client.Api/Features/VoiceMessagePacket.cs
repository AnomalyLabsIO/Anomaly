using Il2Cpp;
using Il2CppVoiceChat;
using Il2CppVoiceChat.Networking;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

[ScriptType]
public class VoiceMessagePacket
{
    private VoiceMessagePacket(ReferenceHub speakerHub, VoiceChatChannel channel, byte[] data, int dataLength, bool speakerNull, bool isOutgoing)
    {
        Speaker = Player.Get(speakerHub);
        SpeakerHub = speakerHub;
        Channel = channel;
        Data = data ?? Array.Empty<byte>();
        DataLength = dataLength;
        SpeakerNull = speakerNull;
        IsOutgoing = isOutgoing;
    }

    [ScriptVisible]
    public Player Speaker { get; }

    public ReferenceHub SpeakerHub { get; }

    [ScriptVisible]
    public VoiceChatChannel Channel { get; }

    public byte[] Data { get; }

    [ScriptVisible]
    public int DataLength { get; }

    [ScriptVisible]
    public bool SpeakerNull { get; }

    [ScriptVisible]
    public bool IsOutgoing { get; }

    [ScriptVisible]
    public bool IsIncoming => !IsOutgoing;

    internal static VoiceMessagePacket FromMessage(VoiceMessage message, bool isOutgoing)
    {
        return new VoiceMessagePacket(message.Speaker, message.Channel, CopyData(message.Data, message.DataLength), message.DataLength, message.SpeakerNull, isOutgoing);
    }

    internal static VoiceMessagePacket FromOutgoingData(ReferenceHub speakerHub, VoiceChatChannel channel, byte[] encodedData, int dataLength)
    {
        return new VoiceMessagePacket(speakerHub, channel, CopyData(encodedData, dataLength), dataLength, speakerHub == null, isOutgoing: true);
    }

    private static byte[] CopyData(byte[] source, int requestedLength)
    {
        if (source == null || requestedLength <= 0)
            return Array.Empty<byte>();

        int copyLength = Math.Min(requestedLength, source.Length);
        byte[] buffer = new byte[copyLength];
        Array.Copy(source, buffer, copyLength);
        return buffer;
    }
}
