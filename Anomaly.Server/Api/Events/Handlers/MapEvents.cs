using Anomaly.Server.Api.Events.Arguments;
using LabApi.Events;

namespace Anomaly.Server.Api.Events.Handlers;

/// <summary>Anomaly facility events. Use LabAPI ServerEvents.MapGenerating to author a recipe.</summary>
public static class MapEvents
{
    /// <summary>Edits the complete selected layout before final placement and snapshot publication.</summary>
    /// <remarks>
    /// Runs synchronously on the server thread before native MapGenerated, for vanilla and custom
    /// layouts. Use the descriptors rather than live Unity objects. Normal LabAPI handler ordering
    /// and exception isolation apply; descriptors become read-only after all handlers return.
    /// </remarks>
    public static event LabEventHandler<FacilityLayoutFinalizingEventArgs> LayoutFinalizing;

    /// <summary>Raised at LabAPI MapGenerated after the frozen server layout is applied.</summary>
    /// <remarks>Remaining native registration stages can still fail and reject the generation.</remarks>
    public static event LabEventHandler RecipeApplied;
    internal static bool HasLayoutFinalizingSubscribers => LayoutFinalizing != null;
    internal static void OnLayoutFinalizing(FacilityLayoutFinalizingEventArgs args) => LayoutFinalizing.InvokeEvent(args);
    internal static void OnRecipeApplied() => RecipeApplied.InvokeEvent();
}
