using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class VoiceEvents
{
    [ScriptEvent(CoalesceLatest = true)]
    public static event AnomalyEventHandler<VoiceMessageSendingEventArgs> Sending;
    [ScriptEvent(CoalesceLatest = true)]
    public static event AnomalyEventHandler<VoiceMessageReceivingEventArgs> Receiving;
    [ScriptEvent(CoalesceLatest = true)]
    public static event AnomalyEventHandler<VoiceMessageSentEventArgs> Sent;
    [ScriptEvent(CoalesceLatest = true)]
    public static event AnomalyEventHandler<VoiceMessageReceivedEventArgs> Received;

    internal static void OnSending(VoiceMessageSendingEventArgs args) => EventInvoker.Invoke(Sending, args);

    internal static void OnReceiving(VoiceMessageReceivingEventArgs args) => EventInvoker.Invoke(Receiving, args);

    internal static void OnSent(VoiceMessageSentEventArgs args) => EventInvoker.Invoke(Sent, args);

    internal static void OnReceived(VoiceMessageReceivedEventArgs args) => EventInvoker.Invoke(Received, args);
}
