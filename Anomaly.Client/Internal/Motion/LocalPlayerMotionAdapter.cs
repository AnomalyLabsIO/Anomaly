using Anomaly.Client.Api.Features;
using Il2Cpp;
using Il2CppPlayerRoles.FirstPersonControl;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Internal.Motion;

/// <summary>
/// Version-specific 14.2.7 FPC adapter behind the public motion facade.
/// Publicized backing-state writes stay isolated here so a future game update
/// can fail closed without changing the mod-facing contract.
/// </summary>
internal static class LocalPlayerMotionAdapter
{
    private static int _mainThreadId;

    internal static void Initialize()
    {
        _mainThreadId = Environment.CurrentManagedThreadId;
        LocalPlayerMotion.SetRuntimeHandler(TryApply, Reset);
    }

    internal static void Shutdown()
    {
        LocalPlayerMotion.ResetSequence();
        LocalPlayerMotion.SetRuntimeHandler(null);
    }

    private static bool TryApply(
        string ownerId,
        MotionDiscontinuity motion,
        out MotionFailure failure)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            failure = MotionFailure.ApplyFailed;
            return false;
        }

        LocalPlayer player = LocalPlayer.Instance;
        if (player?.ReferenceHub == null)
        {
            failure = MotionFailure.NoLocalPlayer;
            return false;
        }

        FirstPersonMovementModule module = player.FirstPersonMovementModule;
        if (module == null)
        {
            failure = MotionFailure.NonFpcRole;
            return false;
        }

        if (!module.ModuleReady || module.Motor == null || module.MouseLook == null)
        {
            failure = MotionFailure.ModuleNotReady;
            return false;
        }

        if (motion.Velocity.sqrMagnitude >
            FpcMotionHandoff.MaximumSpeed * FpcMotionHandoff.MaximumSpeed)
        {
            failure = MotionFailure.InvalidInput;
            return false;
        }

        if (!FpcMotionHandoff.TryBegin(
                ownerId,
                module,
                motion.Velocity,
                motion.Grounded,
                out failure))
        {
            return false;
        }

        try
        {
            Vector3 forward = motion.Forward.normalized;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            if (yaw < 0f) yaw += 360f;
            float pitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, FpcMouseLook.MinimumVer, FpcMouseLook.MaximumVer);

            // FirstPersonMovementModule.Position owns the transform and cached
            // position. Motor.Position resolves to the same setter, but assigning
            // it as well preserves compatibility with derived motor overrides.
            module.Position = motion.Position;
            module.Motor.Position = motion.Position;
            module.Motor.MoveDirection = motion.Velocity;
            module.Motor.Velocity = motion.Velocity;
            module.Motor._maxFallSpeed = Mathf.Max(0f, -motion.Velocity.y);
            module.Motor.ResetFallDamageCooldown();
            module.IsGrounded = motion.Grounded;

            module.MouseLook.CurrentHorizontal = yaw;
            module.MouseLook.CurrentVertical = pitch;
            player.ReferenceHub.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            module.UpdateRelativePosition();
            module.Motor.ReceivedPosition = module.RelativePosition;
            player.CurrentRoomPlayerCache?.ForceUnvalidate();
            MainCameraController.ForceUpdatePosition();

            failure = MotionFailure.None;
            return true;
        }
        catch (Exception ex)
        {
            FpcMotionHandoff.Cancel(ownerId);
            MelonLogger.Error($"LocalPlayerMotion: failed to apply discontinuity {motion.Sequence}: {ex}");
            failure = MotionFailure.ApplyFailed;
            return false;
        }
    }

    private static void Reset(string ownerId)
    {
        FpcMotionHandoff.Cancel(ownerId);
    }
}
