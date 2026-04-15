using MelonLoader;

namespace Anomaly.Client.Events;

internal static class EventInvoker
{
    internal static void Invoke<TEventArgs>(AnomalyEventHandler<TEventArgs> eventHandler, TEventArgs args)
        where TEventArgs : EventArgs
    {
        if (eventHandler == null)
            return;

        foreach (AnomalyEventHandler<TEventArgs> subscriber in eventHandler.GetInvocationList())
        {
            try
            {
                subscriber(args);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Client event handler threw an exception while processing {typeof(TEventArgs).Name}: {ex}");
            }
        }
    }
}
