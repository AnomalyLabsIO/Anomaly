using UnityEngine;

namespace Anomaly.Client.Api.Features;

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
    RuntimeUnavailable,
    NoLocalPlayer,
    NonFpcRole,
    ModuleNotReady,
    InvalidInput,
    StaleSequence,
    ApplyFailed,
    OwnerBusy
}

/// <summary>
/// Atomically applies a local FPC discontinuity through the version-specific
/// Anomaly runtime. Mods do not need to write publicized FPC backing fields.
/// </summary>
public static class LocalPlayerMotion
{
    public const string DefaultOwnerId = "anomaly.default";

    internal delegate bool RuntimeApply(
        string ownerId,
        MotionDiscontinuity motion,
        out MotionFailure failure);

    internal delegate void RuntimeReset(string ownerId);

    private static readonly object Sync = new();
    private static readonly Dictionary<string, uint> LastSequences = new(StringComparer.Ordinal);
    private static RuntimeApply _runtimeApply;
    private static RuntimeReset _runtimeReset;

    public static event Action<MotionDiscontinuity> Applied;

    public static bool TryApply(MotionDiscontinuity motion, out MotionFailure failure)
    {
        return TryApply(DefaultOwnerId, motion, out failure);
    }

    public static bool TryApply(
        string ownerId,
        MotionDiscontinuity motion,
        out MotionFailure failure)
    {
        if (!IsValidOwnerId(ownerId) ||
            !IsFinite(motion.Position) ||
            !IsFinite(motion.Velocity) ||
            !IsFinite(motion.Forward) ||
            motion.Forward.sqrMagnitude < 0.000001f ||
            motion.Sequence == 0)
        {
            failure = MotionFailure.InvalidInput;
            return false;
        }

        lock (Sync)
        {
            if (LastSequences.TryGetValue(ownerId, out uint lastSequence) &&
                motion.Sequence <= lastSequence)
            {
                failure = MotionFailure.StaleSequence;
                return false;
            }

            RuntimeApply apply = _runtimeApply;
            if (apply == null)
            {
                failure = MotionFailure.RuntimeUnavailable;
                return false;
            }

            try
            {
                if (!apply(ownerId, motion, out failure))
                    return false;
            }
            catch
            {
                failure = MotionFailure.ApplyFailed;
                return false;
            }

            LastSequences[ownerId] = motion.Sequence;
        }

        try
        {
            Applied?.Invoke(motion);
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Error($"LocalPlayerMotion.Applied handler threw: {ex}");
        }
        failure = MotionFailure.None;
        return true;
    }

    public static void ResetSequence()
    {
        RuntimeReset reset;
        lock (Sync)
        {
            LastSequences.Clear();
            reset = _runtimeReset;
        }

        TryResetRuntime(reset, null);
    }

    public static void ResetSequence(string ownerId)
    {
        if (!IsValidOwnerId(ownerId))
            throw new ArgumentException(
                "A non-empty motion owner id of at most 128 characters is required.",
                nameof(ownerId));

        RuntimeReset reset;
        lock (Sync)
        {
            LastSequences.Remove(ownerId);
            reset = _runtimeReset;
        }

        TryResetRuntime(reset, ownerId);
    }

    internal static void SetRuntimeHandler(RuntimeApply handler, RuntimeReset resetHandler = null)
    {
        lock (Sync)
        {
            _runtimeApply = handler;
            _runtimeReset = resetHandler;
        }
    }

    private static void TryResetRuntime(RuntimeReset reset, string ownerId)
    {
        if (reset == null)
            return;

        try
        {
            reset(ownerId);
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Error($"LocalPlayerMotion runtime reset failed: {ex}");
        }
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z);
    }

    private static bool IsValidOwnerId(string ownerId)
    {
        return !string.IsNullOrWhiteSpace(ownerId) && ownerId.Length <= 128;
    }
}
