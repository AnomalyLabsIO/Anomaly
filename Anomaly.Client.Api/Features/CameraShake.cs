using Il2CppCameraShaking;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Local camera-shake helper. Wraps <see cref="CameraShakeController"/>'s
/// singleton and exposes a stable entry point for mods to apply shake effects
/// (recoil, footsteps, explosion feedback, etc.) without coupling to the
/// game's controller class directly.
/// </summary>
/// <remarks>
/// SCP:SL ships several built-in <see cref="IShakeEffect"/> types in
/// <c>Il2CppCameraShaking</c>: <see cref="LookatShake"/>, <see cref="RecoilShake"/>,
/// <see cref="TrackerShake"/>, <see cref="ElevatorShake"/>, <see cref="HeadbobShake"/>.
/// Construct one of those with the parameters you want and pass it to
/// <see cref="Apply(IShakeEffect)"/>. For most use cases <see cref="RecoilShake"/>
/// is the closest to "generic short shake".
/// </remarks>
public static class CameraShake
{
    /// <summary>
    /// True if the camera-shake controller's singleton is currently
    /// initialized. Mods that fire shakes from <see cref="ClientEvents.Ready"/>
    /// or later can usually skip this check.
    /// </summary>
    public static bool IsAvailable => CameraShakeController.Singleton != null;

    /// <summary>
    /// Adds <paramref name="effect"/> to the active shake stack. Effects are
    /// reference-tracked by the controller and removed when their internal
    /// timer elapses (see the individual <see cref="IShakeEffect"/> implementations).
    /// </summary>
    /// <returns>True if the effect was queued, false if the controller singleton hasn't initialized yet.</returns>
    public static bool Apply(IShakeEffect effect)
    {
        if (effect == null)
            return false;

        if (CameraShakeController.Singleton == null)
            return false;

        try
        {
            CameraShakeController.AddEffect(effect);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
