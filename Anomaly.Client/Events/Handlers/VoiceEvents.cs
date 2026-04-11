using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class VoiceEvents
{
    public static event AnEventHandler<VoiceMessageSendingEventArgs> Sending;
    public static event AnEventHandler<VoiceMessageReceivingEventArgs> Receiving;
    public static event AnEventHandler<VoiceMessageSentEventArgs> Sent;
    public static event AnEventHandler<VoiceMessageReceivedEventArgs> Received;

    internal static void OnSending(VoiceMessageSendingEventArgs args) => EventInvoker.Invoke(Sending, args);

    internal static void OnReceiving(VoiceMessageReceivingEventArgs args) => EventInvoker.Invoke(Receiving, args);

    internal static void OnSent(VoiceMessageSentEventArgs args) => EventInvoker.Invoke(Sent, args);

    internal static void OnReceived(VoiceMessageReceivedEventArgs args) => EventInvoker.Invoke(Received, args);
}
