using Il2CppCameraShaking;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when a camera shake effect is about to be applied to the local view
/// (<c>CameraShakeController.AddEffect</c>). Cancelling drops the effect on
/// the floor; nothing about the server or other clients changes.
/// </summary>
public class CameraShakingEventArgs : EventArgs, ICancellableEvent
{
    public CameraShakingEventArgs(IShakeEffect effect)
    {
        Effect = effect;
    }

    /// <summary>The shake effect about to be enqueued.</summary>
    public IShakeEffect Effect { get; }

    public bool IsAllowed { get; set; } = true;
}
