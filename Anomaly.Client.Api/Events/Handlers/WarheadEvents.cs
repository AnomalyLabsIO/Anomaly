using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// Alpha warhead lifecycle events. Hooked from
/// <see cref="Il2Cpp.AlphaWarheadController"/>'s static
/// <c>OnProgressChanged</c> and <c>OnDetonated</c> events.
/// </summary>
public static class WarheadEvents
{
    /// <summary>Fires when the countdown starts or stops (cancellation / detonation).</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<WarheadProgressChangedEventArgs> ProgressChanged;

    /// <summary>Fires once when the warhead actually detonates.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<WarheadDetonatedEventArgs> Detonated;

    internal static void OnProgressChanged(WarheadProgressChangedEventArgs args) =>
        EventInvoker.Invoke(ProgressChanged, args);

    internal static void OnDetonated() =>
        EventInvoker.Invoke(Detonated, new WarheadDetonatedEventArgs());
}
