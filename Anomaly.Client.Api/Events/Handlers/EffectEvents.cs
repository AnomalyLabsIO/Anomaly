using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class EffectEvents
{
    public static event AnomalyEventHandler<EffectEnabledEventArgs> Enabled;
    public static event AnomalyEventHandler<EffectDisabledEventArgs> Disabled;
    public static event AnomalyEventHandler<EffectIntensityChangedEventArgs> IntensityChanged;

    internal static void OnEnabled(EffectEnabledEventArgs args) => EventInvoker.Invoke(Enabled, args);

    internal static void OnDisabled(EffectDisabledEventArgs args) => EventInvoker.Invoke(Disabled, args);

    internal static void OnIntensityChanged(EffectIntensityChangedEventArgs args) => EventInvoker.Invoke(IntensityChanged, args);
}
