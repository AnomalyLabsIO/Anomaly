using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// Generator lifecycle events. Hooked from
/// <see cref="Il2CppMapGeneration.Distributors.Scp079Generator"/>'s static
/// events.
/// </summary>
public static class GeneratorEvents
{
    /// <summary>Fires once per generator the moment it transitions to <c>Engaged</c>.</summary>
    public static event AnomalyEventHandler<GeneratorEngagedEventArgs> Engaged;

    internal static void OnEngaged(GeneratorEngagedEventArgs args) =>
        EventInvoker.Invoke(Engaged, args);
}
