using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class EffectEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<EffectEnabledEventArgs> Enabled;
    [ScriptEvent]
    public static event AnomalyEventHandler<EffectDisabledEventArgs> Disabled;
    [ScriptEvent]
    public static event AnomalyEventHandler<EffectIntensityChangedEventArgs> IntensityChanged;

    internal static void OnEnabled(EffectEnabledEventArgs args) => EventInvoker.Invoke(Enabled, args);

    internal static void OnDisabled(EffectDisabledEventArgs args) => EventInvoker.Invoke(Disabled, args);

    internal static void OnIntensityChanged(EffectIntensityChangedEventArgs args) => EventInvoker.Invoke(IntensityChanged, args);
}
