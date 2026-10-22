using MelonLoader;

namespace Anomaly.Client.Api.Events;

internal static class EventInvoker
{
    internal static void Invoke(Action eventHandler, string context)
    {
        if (eventHandler == null)
            return;

        foreach (Action subscriber in eventHandler.GetInvocationList())
            InvokeSubscriber(subscriber, context);
    }

    internal static void Invoke<T>(Action<T> eventHandler, T args, string context)
    {
        if (eventHandler == null)
            return;

        foreach (Action<T> subscriber in eventHandler.GetInvocationList())
            InvokeSubscriber(() => subscriber(args), context);
    }

    internal static void Invoke<TEventArgs>(AnomalyEventHandler<TEventArgs> eventHandler, TEventArgs args)
        where TEventArgs : EventArgs
    {
        if (eventHandler == null)
            return;

        ICancellableEvent cancellable = args as ICancellableEvent;
        bool cancelled = cancellable != null && !cancellable.IsAllowed;
        foreach (AnomalyEventHandler<TEventArgs> subscriber in eventHandler.GetInvocationList())
        {
            try
            {
                subscriber(args);
            }
            catch (Exception ex)
            {
                try
                {
                    MelonLogger.Error(
                        $"Client event handler threw an exception while processing {typeof(TEventArgs).Name}: {ex}");
                }
                catch
                {
                    // Metadata/unit-test hosts may not have a usable loader
                    // logger. Logging must never suppress later subscribers.
                }
            }
            finally
            {
                if (cancellable != null)
                {
                    cancelled |= !cancellable.IsAllowed;
                    if (cancelled)
                        cancellable.IsAllowed = false;
                }
            }
        }
    }

    private static void InvokeSubscriber(Action subscriber, string context)
    {
        try
        {
            subscriber();
        }
        catch (Exception ex)
        {
            try
            {
                MelonLogger.Error($"Client event handler threw while processing {context}: {ex}");
            }
            catch
            {
                // Logging must never suppress later subscribers.
            }
        }
    }
}
