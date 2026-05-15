namespace Anomaly.Client.Api.Events;

/// <summary>
/// Marks an EventArgs as cancellable. Subscribers may set <see cref="IsAllowed"/>
/// to <c>false</c> to veto the action the event represents. The dispatching
/// Harmony patch reads this after invoking subscribers and skips the original
/// behavior when not allowed.
/// </summary>
public interface ICancellableEvent
{
    /// <summary>
    /// <c>true</c> by default. Set to <c>false</c> from a subscriber to cancel
    /// the underlying action (e.g., suppress a local input, hide a hint, skip
    /// a network command). Once any subscriber sets this to <c>false</c>, later
    /// subscribers still run but cannot un-cancel.
    /// </summary>
    bool IsAllowed { get; set; }
}
