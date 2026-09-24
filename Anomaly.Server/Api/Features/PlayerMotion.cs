using System;
using LabApi.Features.Wrappers;
using Anomaly.Server.Api.Events.Handlers;
using System.Collections.Generic;
using Anomaly.Server.Internal.Motion;
using PlayerRoles.FirstPersonControl;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Api.Features
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
        private static readonly object Sync = new object();
        private static readonly Dictionary<ReferenceHub, Dictionary<string, uint>> LastSequences =
            new Dictionary<ReferenceHub, Dictionary<string, uint>>();

        /// <summary>
        /// Applies position, look and momentum together on the game thread. Use a stable
        /// explicit owner ID and strictly increasing nonzero sequence numbers for that
        /// owner/player pair. A competing owner cannot steal an active handoff.
        /// </summary>
        public static bool TryApply(string ownerId, Player player, MotionDiscontinuity motion, out MotionFailure failure) =>
            TryApplyCore(ownerId, player?.ReferenceHub, motion, out failure);

        private static bool TryApplyCore(
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
                float.IsInfinity(motion.Forward.sqrMagnitude) ||
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
                    failure = MotionFailure.ApplyFailed;
                    try
                    {
                        Logger.Error(
                            $"PlayerMotion: failed to apply discontinuity {motion.Sequence} for " +
                            $"{player.LoggedNameFromRefHub()}: {ex}");
                    }
                    catch { }
                    return false;
                }
            }

            ClientEvents.OnMotionApplied(player, ownerId, motion);

            failure = MotionFailure.None;
            return true;
        }

        internal static void Reset(ReferenceHub player)
        {
            if (player == null) return;
            lock (Sync)
                LastSequences.Remove(player);
            FpcMotionHandoff.Cancel(player);
        }

        /// <summary>Releases only this owner's motion state for the player.</summary>
        public static void Reset(Player player, string ownerId) => ResetOwner(player?.ReferenceHub, ownerId);

        private static void ResetOwner(ReferenceHub player, string ownerId)
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
