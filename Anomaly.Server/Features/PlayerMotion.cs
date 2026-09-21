using System;
using System.Collections.Generic;
using Anomaly.Server.Internal.Motion;
using PlayerRoles.FirstPersonControl;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Features
{
    public readonly struct MotionDiscontinuity
    {
        public MotionDiscontinuity(
            Vector3 position,
            Vector3 velocity,
            Vector3 forward,
            uint sequence,
            bool grounded = false)
        {
            Position = position;
            Velocity = velocity;
            Forward = forward;
            Sequence = sequence;
            Grounded = grounded;
        }

        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public Vector3 Forward { get; }
        public uint Sequence { get; }
        public bool Grounded { get; }
    }

    public enum MotionFailure : byte
    {
        None = 0,
        NoPlayer,
        NonFpcRole,
        ModuleNotReady,
        InvalidInput,
        StaleSequence,
        ApplyFailed,
        OwnerBusy
    }

    /// <summary>
    /// Atomic authoritative FPC position/look/momentum discontinuities.
    /// </summary>
    public static class PlayerMotion
    {
        public const string DefaultOwnerId = "anomaly.default";

        private static readonly object Sync = new object();
        private static readonly Dictionary<ReferenceHub, Dictionary<string, uint>> LastSequences =
            new Dictionary<ReferenceHub, Dictionary<string, uint>>();

        public static event Action<ReferenceHub, MotionDiscontinuity> Applied;

        public static bool TryApply(
            ReferenceHub player,
            MotionDiscontinuity motion,
            out MotionFailure failure)
        {
            return TryApply(DefaultOwnerId, player, motion, out failure);
        }

        public static bool TryApply(
            string ownerId,
            ReferenceHub player,
            MotionDiscontinuity motion,
            out MotionFailure failure)
        {
            if (player == null)
            {
                failure = MotionFailure.NoPlayer;
                return false;
            }
            if (!IsValidOwnerId(ownerId) ||
                !IsFinite(motion.Position) ||
                !IsFinite(motion.Velocity) ||
                !IsFinite(motion.Forward) ||
                motion.Velocity.sqrMagnitude >
                    FpcMotionHandoff.MaximumSpeed * FpcMotionHandoff.MaximumSpeed ||
                motion.Forward.sqrMagnitude < 0.000001f ||
                motion.Sequence == 0)
            {
                failure = MotionFailure.InvalidInput;
                return false;
            }

            var role = player.roleManager?.CurrentRole as IFpcRole;
            if (role == null)
            {
                failure = MotionFailure.NonFpcRole;
                return false;
            }

            FirstPersonMovementModule module = role.FpcModule;
            if (module == null || !module.ModuleReady || module.Motor == null || module.MouseLook == null)
            {
                failure = MotionFailure.ModuleNotReady;
                return false;
            }

            lock (Sync)
            {
                if (LastSequences.TryGetValue(player, out Dictionary<string, uint> ownerSequences) &&
                    ownerSequences.TryGetValue(ownerId, out uint last) &&
                    motion.Sequence <= last)
                {
                    failure = MotionFailure.StaleSequence;
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

                    module.ServerOverridePosition(motion.Position);
                    module.Motor.Position = motion.Position;
                    module.Motor.MoveDirection = motion.Velocity;
                    module.Motor.Velocity = motion.Velocity;
                    module.Motor._maxFallSpeed = Mathf.Max(0f, -motion.Velocity.y);
                    module.Motor.ResetFallDamageCooldown();
                    module.IsGrounded = motion.Grounded;
                    module.UpdateRelativePosition();
                    module.Motor.ReceivedPosition = module.RelativePosition;
                    player.CurrentRoomPlayerCache?.ForceUnvalidate();
                    // 14.2.7's override vector is (vertical pitch, horizontal
                    // yaw), despite the common Vector2(yaw, pitch) convention.
                    module.ServerOverrideRotation(new Vector2(pitch, yaw));

                    if (ownerSequences == null)
                    {
                        ownerSequences = new Dictionary<string, uint>(StringComparer.Ordinal);
                        LastSequences[player] = ownerSequences;
                    }
                    ownerSequences[ownerId] = motion.Sequence;
                }
                catch (Exception ex)
                {
                    FpcMotionHandoff.Cancel(player, ownerId);
                    Logger.Error(
                        $"PlayerMotion: failed to apply discontinuity {motion.Sequence} for " +
                        $"{player.LoggedNameFromRefHub()}: {ex}");
                    failure = MotionFailure.ApplyFailed;
                    return false;
                }
            }

            Delegate[] subscribers = Applied?.GetInvocationList() ?? Array.Empty<Delegate>();
            foreach (Delegate subscriber in subscribers)
            {
                try
                {
                    ((Action<ReferenceHub, MotionDiscontinuity>)subscriber)(player, motion);
                }
                catch (Exception ex)
                {
                    try { Logger.Error($"PlayerMotion.Applied handler threw: {ex}"); }
                    catch { /* logging must not suppress later subscribers */ }
                }
            }

            failure = MotionFailure.None;
            return true;
        }

        public static void Reset(ReferenceHub player)
        {
            if (player == null) return;
            lock (Sync)
                LastSequences.Remove(player);
            FpcMotionHandoff.Cancel(player);
        }

        public static void Reset(ReferenceHub player, string ownerId)
        {
            if (player == null) return;
            if (!IsValidOwnerId(ownerId))
                throw new ArgumentException(
                    "A non-empty motion owner id of at most 128 characters is required.",
                    nameof(ownerId));

            lock (Sync)
            {
                if (LastSequences.TryGetValue(
                        player,
                        out Dictionary<string, uint> ownerSequences))
                {
                    ownerSequences.Remove(ownerId);
                    if (ownerSequences.Count == 0)
                        LastSequences.Remove(player);
                }
            }

            FpcMotionHandoff.Cancel(player, ownerId);
        }

        internal static void ResetAll()
        {
            lock (Sync)
                LastSequences.Clear();
            FpcMotionHandoff.ResetAll();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsValidOwnerId(string ownerId)
        {
            return !string.IsNullOrWhiteSpace(ownerId) && ownerId.Length <= 128;
        }
    }
}
