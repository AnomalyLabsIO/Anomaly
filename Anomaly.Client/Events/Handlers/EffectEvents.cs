using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class EffectEvents
{
    public static event AnEventHandler<EffectEnabledEventArgs> Enabled;
    public static event AnEventHandler<EffectDisabledEventArgs> Disabled;
    public static event AnEventHandler<EffectIntensityChangedEventArgs> IntensityChanged;

    internal static void OnEnabled(EffectEnabledEventArgs args) => EventInvoker.Invoke(Enabled, args);

    internal static void OnDisabled(EffectDisabledEventArgs args) => EventInvoker.Invoke(Disabled, args);

    internal static void OnIntensityChanged(EffectIntensityChangedEventArgs args) => EventInvoker.Invoke(IntensityChanged, args);
}
