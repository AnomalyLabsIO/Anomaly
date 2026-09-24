using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Anomaly.Server.Api.Features;
using PlayerRoles.FirstPersonControl;
using UnityEngine;

namespace Anomaly.Server.Internal.Motion
{
    /// <summary>
    /// Short, owner-scoped velocity handoff for explicit FPC discontinuities.
    /// The stock 14.2.7 motor owns movement again after the bounded interval.
    /// </summary>
    internal static class FpcMotionHandoff
    {
        private const int MinimumMoveCycles = 3;
        private const int MaximumMoveCycles = 32;
        private const double TargetDurationSeconds = 0.16;
        private const double MaximumDurationSeconds = 0.25;
        private const float MaximumDeltaTime = 0.05f;
        internal const float MaximumSpeed = 100f;

        private static readonly object Sync = new object();
        private static readonly Dictionary<ReferenceHub, State> Active =
            new Dictionary<ReferenceHub, State>();
        private static int _hasActive;

        internal static bool TryBegin(
            string ownerId,
            FirstPersonMovementModule module,
            Vector3 velocity,
            bool grounded,
            out MotionFailure failure)
        {
            FpcMotor motor = module?.Motor;
            ReferenceHub hub = motor?.Hub;
            if (module == null ||
                !module.ModuleReady ||
                motor == null ||
                hub == null ||
                !ReferenceEquals(motor.MainModule, module))
            {
                failure = MotionFailure.ModuleNotReady;
                return false;
            }

            lock (Sync)
            {
                if (Active.TryGetValue(hub, out State existing))
                {
                    if (ElapsedSeconds(existing.StartedTimestamp) >= MaximumDurationSeconds)
                    {
                        RemoveLocked(hub);
                    }
                    else if (!string.Equals(
                                 existing.OwnerId,
                                 ownerId,
                                 StringComparison.Ordinal))
                    {
                        failure = MotionFailure.OwnerBusy;
                        return false;
                    }
                }

                if (velocity.sqrMagnitude < 0.000001f)
                {
                    RemoveLocked(hub);
                    failure = MotionFailure.None;
                    return true;
                }

                Active[hub] = new State
                {
                    OwnerId = ownerId,
                    Hub = hub,
                    Module = module,
                    Motor = motor,
                    Velocity = velocity,
                    Airborne = !grounded || velocity.y > 0.01f,
                    StartedTimestamp = Stopwatch.GetTimestamp()
                };
                Volatile.Write(ref _hasActive, 1);
            }

            failure = MotionFailure.None;
            return true;
        }

        internal static void Cancel(ReferenceHub hub, string ownerId = null)
        {
            if (hub == null)
                return;

            lock (Sync)
            {
                if (!Active.TryGetValue(hub, out State state) ||
                    (ownerId != null &&
                     !string.Equals(state.OwnerId, ownerId, StringComparison.Ordinal)))
                {
                    return;
                }

                RemoveLocked(hub);
            }
        }

        internal static void ResetAll()
        {
            lock (Sync)
            {
                Active.Clear();
                Volatile.Write(ref _hasActive, 0);
            }
        }

        internal static void ApplyFrame(FpcMotor motor, ref Vector3 frameMove)
        {
            if (Volatile.Read(ref _hasActive) == 0)
                return;

            ReferenceHub hub = motor?.Hub;
            if (hub == null)
                return;

            lock (Sync)
            {
                if (!Active.TryGetValue(hub, out State state) ||
                    !ReferenceEquals(state.Motor, motor))
                {
                    return;
                }

                double elapsed = ElapsedSeconds(state.StartedTimestamp);
                FirstPersonMovementModule module = state.Module;
                if (elapsed >= MaximumDurationSeconds ||
                    state.Hub == null ||
                    module == null ||
                    !module.ModuleReady ||
                    module.Motor == null ||
                    !ReferenceEquals(module.Motor, motor) ||
                    module.Noclip == null ||
                    module.Noclip.IsActive)
                {
                    RemoveLocked(hub);
                    return;
                }

                float deltaTime = Time.deltaTime;
                if (float.IsNaN(deltaTime) ||
                    float.IsInfinity(deltaTime) ||
                    deltaTime <= 0f)
                {
                    RemoveLocked(hub);
                    return;
                }
                deltaTime = Mathf.Min(deltaTime, MaximumDeltaTime);

                if (!state.Airborne &&
                    (!module.IsGrounded || state.Velocity.y > 0.01f))
                {
                    state.Airborne = true;
                }

                Vector3 frameVelocity = state.Velocity;
                if (state.Airborne)
                {
                    if (motor.GravityController == null)
                    {
                        RemoveLocked(hub);
                        return;
                    }

                    Vector3 gravity = motor.GravityController.Gravity;
                    if (!IsFinite(gravity))
                    {
                        RemoveLocked(hub);
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

                // UpdateFloating contributes the other half of gravity after
                // Move returns, preserving the handoff's final vertical speed.
                motor.MoveDirection = frameVelocity;

                state.MoveCycles++;
                bool targetReached =
                    state.MoveCycles >= MinimumMoveCycles &&
                    elapsed >= TargetDurationSeconds;
                if (targetReached || state.MoveCycles >= MaximumMoveCycles)
                    RemoveLocked(hub);
            }
        }

        private static void RemoveLocked(ReferenceHub hub)
        {
            Active.Remove(hub);
            if (Active.Count == 0)
                Volatile.Write(ref _hasActive, 0);
        }

        private static double ElapsedSeconds(long startedTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startedTimestamp) /
                   (double)Stopwatch.Frequency;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private sealed class State
        {
            internal string OwnerId;
            internal ReferenceHub Hub;
            internal FirstPersonMovementModule Module;
            internal FpcMotor Motor;
            internal Vector3 Velocity;
            internal bool Airborne;
            internal long StartedTimestamp;
            internal int MoveCycles;
        }
    }
}
