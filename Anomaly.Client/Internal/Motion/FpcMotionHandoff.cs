using System.Diagnostics;
using System.Threading;
using Anomaly.Client.Api.Features;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppPlayerRoles.FirstPersonControl;
using UnityEngine;

namespace Anomaly.Client.Internal.Motion;

/// <summary>
/// Carries a mapped FPC velocity through the short interval in which the
/// 14.2.7 motor would otherwise replace its horizontal component with input.
/// State exists only after an explicit LocalPlayerMotion request.
/// </summary>
internal static class FpcMotionHandoff
{
    private const int MinimumMoveCycles = 3;
    private const int MaximumMoveCycles = 32;
    private const double TargetDurationSeconds = 0.16;
    private const double MaximumDurationSeconds = 0.25;
    private const float MaximumDeltaTime = 0.05f;
    internal const float MaximumSpeed = 100f;

    private static readonly object Sync = new();
    private static State _active;
    private static int _hasActive;

    internal static bool TryBegin(
        string ownerId,
        FirstPersonMovementModule module,
        Vector3 velocity,
        bool grounded,
        out MotionFailure failure)
    {
        if (module?.Motor == null || !module.ModuleReady)
        {
            failure = MotionFailure.ModuleNotReady;
            return false;
        }

        IntPtr motorPointer = GetPointer(module.Motor);
        if (motorPointer == IntPtr.Zero)
        {
            failure = MotionFailure.ModuleNotReady;
            return false;
        }

        lock (Sync)
        {
            ExpireStaleLocked();
            if (_active != null &&
                !string.Equals(_active.OwnerId, ownerId, StringComparison.Ordinal))
            {
                failure = MotionFailure.OwnerBusy;
                return false;
            }

            if (velocity.sqrMagnitude < 0.000001f)
            {
                ClearLocked();
                failure = MotionFailure.None;
                return true;
            }

            _active = new State
            {
                OwnerId = ownerId,
                MotorPointer = motorPointer,
                Module = module,
                Velocity = velocity,
                Airborne = !grounded || velocity.y > 0.01f,
                StartedTimestamp = Stopwatch.GetTimestamp()
            };
            Volatile.Write(ref _hasActive, 1);
        }

        failure = MotionFailure.None;
        return true;
    }

    internal static void Cancel(string ownerId)
    {
        lock (Sync)
        {
            if (_active == null ||
                (ownerId != null &&
                 !string.Equals(_active.OwnerId, ownerId, StringComparison.Ordinal)))
            {
                return;
            }

            ClearLocked();
        }
    }

    internal static void Cancel(FirstPersonMovementModule module)
    {
        if (module?.Motor == null)
            return;

        IntPtr motorPointer = GetPointer(module.Motor);
        lock (Sync)
        {
            if (_active != null && _active.MotorPointer == motorPointer)
                ClearLocked();
        }
    }

    internal static void ApplyFrame(FpcMotor motor, ref Vector3 frameMove)
    {
        if (motor == null || Volatile.Read(ref _hasActive) == 0)
            return;

        lock (Sync)
        {
            State state = _active;
            if (state == null || state.MotorPointer != GetPointer(motor))
                return;

            double elapsed = ElapsedSeconds(state.StartedTimestamp);
            FirstPersonMovementModule module = state.Module;
            if (elapsed >= MaximumDurationSeconds ||
                module == null ||
                !module.ModuleReady ||
                module.Motor == null ||
                GetPointer(module.Motor) != state.MotorPointer ||
                module.Noclip == null ||
                module.Noclip.IsActive)
            {
                ClearLocked();
                return;
            }

            float deltaTime = Time.deltaTime;
            if (!float.IsFinite(deltaTime) || deltaTime <= 0f)
            {
                ClearLocked();
                return;
            }
            deltaTime = Mathf.Min(deltaTime, MaximumDeltaTime);

            if (!state.Airborne &&
                (module.CharController == null ||
                 !module.CharController.isGrounded ||
                 state.Velocity.y > 0.01f))
            {
                state.Airborne = true;
            }

            Vector3 frameVelocity = state.Velocity;
            if (state.Airborne)
            {
                Vector3 gravity = motor.GravityController?.Gravity ?? Physics.gravity;
                if (!IsFinite(gravity))
                {
                    ClearLocked();
                    return;
                }

                Vector3 halfGravityStep = gravity * (0.5f * deltaTime);
                frameVelocity += halfGravityStep;
                state.Velocity += gravity * deltaTime;
            }
            else if (frameVelocity.y < 0f)
            {
                frameVelocity.y = 0f;
                state.Velocity.y = 0f;
            }

            frameMove = frameVelocity * deltaTime;

            // UpdateFloating adds its second half-gravity step after Move()
            // returns. Publishing the midpoint velocity here leaves the stock
            // motor at the same end-of-step vertical velocity as our state.
            motor.MoveDirection = frameVelocity;

            state.MoveCycles++;
            bool targetReached =
                state.MoveCycles >= MinimumMoveCycles &&
                elapsed >= TargetDurationSeconds;
            if (targetReached || state.MoveCycles >= MaximumMoveCycles)
                ClearLocked();
        }
    }

    private static void ExpireStaleLocked()
    {
        if (_active != null &&
            ElapsedSeconds(_active.StartedTimestamp) >= MaximumDurationSeconds)
        {
            ClearLocked();
        }
    }

    private static void ClearLocked()
    {
        _active = null;
        Volatile.Write(ref _hasActive, 0);
    }

    private static double ElapsedSeconds(long startedTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startedTimestamp) /
               (double)Stopwatch.Frequency;
    }

    private static IntPtr GetPointer(FpcMotor motor)
    {
        return motor == null
            ? IntPtr.Zero
            : IL2CPP.Il2CppObjectBaseToPtr((Il2CppObjectBase)(object)motor);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z);
    }

    private sealed class State
    {
        internal string OwnerId;
        internal IntPtr MotorPointer;
        internal FirstPersonMovementModule Module;
        internal Vector3 Velocity;
        internal bool Airborne;
        internal long StartedTimestamp;
        internal int MoveCycles;
    }
}
