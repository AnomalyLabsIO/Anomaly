using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Internal;
using MelonLoader;

namespace Anomaly.Client.Api.Scheduling;

/// <summary>
/// Unified client-side scheduler for delayed callbacks, recurring ticks, and
/// named cooldowns. Ticked every frame from <c>ClientRuntime.Update</c> via the
/// internal <c>SchedulerTick</c>. All timing is advanced using
/// <see cref="UnityEngine.Time.unscaledDeltaTime"/> so pauses in game time do not
/// delay scheduled work.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cooldowns</b> are addressed by a string key, which must be a syntactically
/// valid dotted-lowercase namespace (e.g. <c>"mymod.ability"</c>). Use your
/// mod id as the prefix so keys don't collide across mods.
/// </para>
/// <para>
/// Scheduled tasks and cooldowns are automatically cleared on client disconnect
/// and scene load by <c>ClientRuntime</c>; don't schedule cross-session work
/// expecting it to survive either boundary.
/// </para>
/// <para>
/// Server-authoritative cooldowns (e.g. custom ability cooldowns broadcast to the
/// owning player) will drive this same store in Phase B — storing them here lets
/// client-side UI read the remaining time via <see cref="GetCooldownRemaining"/>
/// without duplicating state.
/// </para>
/// </remarks>
public static class Scheduler
{
    private static readonly List<ScheduledTask> _tasks = new List<ScheduledTask>();
    private static readonly Dictionary<string, double> _cooldowns = new Dictionary<string, double>(StringComparer.Ordinal);

    private static bool _paused;
    private static readonly List<ScheduledTask> _pendingRemoval = new List<ScheduledTask>();

    /// <summary>
    /// Registers a one-shot callback to fire after <paramref name="delay"/> elapses.
    /// </summary>
    public static ScheduledTask In(TimeSpan delay, Action callback) =>
        In(delay, callback, ownerId: null);

    /// <summary>
    /// Registers a one-shot callback, attributing it to <paramref name="ownerId"/>
    /// for diagnostics. <paramref name="ownerId"/> is informational only — no
    /// validation is performed.
    /// </summary>
    internal static ScheduledTask In(TimeSpan delay, Action callback, string ownerId)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        var task = new ScheduledTask(ScheduledTask.TaskKind.OneShot,
            delaySeconds: Math.Max(0, delay.TotalSeconds),
            intervalSeconds: 0,
            callback: callback,
            ownerId: ownerId);
        _tasks.Add(task);
        return task;
    }

    /// <summary>
    /// Registers a recurring callback to fire every <paramref name="interval"/>.
    /// If <paramref name="firstDelay"/> is default, the first invocation happens
    /// one <paramref name="interval"/> from now.
    /// </summary>
    public static ScheduledTask Every(TimeSpan interval, Action callback, TimeSpan firstDelay = default) =>
        Every(interval, callback, firstDelay, ownerId: null);

    /// <summary>
    /// Registers a recurring callback, attributing it to <paramref name="ownerId"/>.
    /// </summary>
    internal static ScheduledTask Every(TimeSpan interval, Action callback, TimeSpan firstDelay, string ownerId)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), "Must be positive.");

        double intervalSec = interval.TotalSeconds;
        double firstDelaySec = firstDelay == default ? intervalSec : Math.Max(0, firstDelay.TotalSeconds);

        var task = new ScheduledTask(ScheduledTask.TaskKind.Recurring,
            delaySeconds: firstDelaySec,
            intervalSeconds: intervalSec,
            callback: callback,
            ownerId: ownerId);
        _tasks.Add(task);
        return task;
    }

    /// <summary>
    /// Starts or refreshes a named cooldown. The key must be prefixed by a
    /// non-reserved mod id.
    /// </summary>
    public static void StartCooldown(string key, TimeSpan duration)
    {
        ValidateCooldownKey(key);
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        _cooldowns[key] = duration.TotalSeconds;
    }

    /// <summary>
    /// Returns remaining cooldown time for <paramref name="key"/>. Zero if the
    /// cooldown has elapsed or was never set.
    /// </summary>
    public static TimeSpan GetCooldownRemaining(string key)
    {
        if (string.IsNullOrEmpty(key)) return TimeSpan.Zero;
        if (_cooldowns.TryGetValue(key, out var remaining) && remaining > 0)
            return TimeSpan.FromSeconds(remaining);
        return TimeSpan.Zero;
    }

    /// <summary>True iff the cooldown has a positive remaining time.</summary>
    public static bool IsOnCooldown(string key) => GetCooldownRemaining(key) > TimeSpan.Zero;

    /// <summary>Clears a cooldown immediately.</summary>
    public static void ClearCooldown(string key)
    {
        if (!string.IsNullOrEmpty(key)) _cooldowns.Remove(key);
    }

    /// <summary>
    /// Pauses advancement of every scheduled task and cooldown. Resume with
    /// <see cref="ResumeAll"/>. Used during round transitions so timing isn't
    /// corrupted by scene loads.
    /// </summary>
    internal static void PauseAll() => _paused = true;

    internal static void ResumeAll() => _paused = false;

    /// <summary>
    /// Cancels all tasks and clears all cooldowns. Intended for catastrophic
    /// reset paths (disconnect, application shutdown). Individual plugins should
    /// <see cref="ScheduledTask.Cancel"/> their own handles.
    /// </summary>
    internal static void ClearAll()
    {
        foreach (var t in _tasks) t.Cancel();
        _tasks.Clear();
        _cooldowns.Clear();
        _pendingRemoval.Clear();
        _paused = false;
    }

    /// <summary>
    /// Cancels framework-managed work belonging to one managed module. Peer
    /// mods cannot call this boundary; they retain and cancel their own task
    /// handles instead.
    /// </summary>
    internal static void ClearOwner(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            return;

        for (var i = _tasks.Count - 1; i >= 0; i--)
        {
            var task = _tasks[i];
            if (string.Equals(task.OwnerId, ownerId, StringComparison.Ordinal))
            {
                task.Cancel();
                _tasks.RemoveAt(i);
            }
        }

        var cooldownsToRemove = new List<string>();
        string prefix = ownerId + ".";
        foreach (var key in _cooldowns.Keys)
        {
            if (string.Equals(key, ownerId, StringComparison.Ordinal)
                || key.StartsWith(prefix, StringComparison.Ordinal))
                cooldownsToRemove.Add(key);
        }
        foreach (var key in cooldownsToRemove)
            _cooldowns.Remove(key);
    }

    /// <summary>
    /// Advances all scheduled tasks and cooldowns by one frame. Called from
    /// <c>ClientRuntime.Update</c> via the internal <c>SchedulerTick</c>.
    /// </summary>
    internal static void Tick(double deltaSeconds)
    {
        if (_paused || deltaSeconds <= 0) return;

        // Cooldowns: decrement, remove elapsed.
        if (_cooldowns.Count > 0)
        {
            List<string> expired = null;
            // Snapshot the keys because updating an existing Dictionary value can
            // invalidate its enumerator on supported runtimes.
            foreach (string key in new List<string>(_cooldowns.Keys))
            {
                if (!_cooldowns.TryGetValue(key, out double current))
                    continue;
                double remaining = current - deltaSeconds;
                if (remaining <= 0)
                {
                    (expired ??= new List<string>()).Add(key);
                }
                else
                {
                    _cooldowns[key] = remaining;
                }
            }
            if (expired != null)
                foreach (var key in expired) _cooldowns.Remove(key);
        }

        // Tasks: iterate snapshot-safe; allow callbacks to register new tasks without
        // affecting this frame's iteration.
        var snapshot = _tasks.ToArray();
        foreach (ScheduledTask task in snapshot)
        {
            if (task.IsCanceled || task.Paused)
            {
                if (task.IsCanceled) _pendingRemoval.Add(task);
                continue;
            }

            task.DelaySeconds -= deltaSeconds;
            if (task.DelaySeconds > 0) continue;

            InvokeSafely(task);

            if (task.Kind == ScheduledTask.TaskKind.OneShot)
            {
                task.MarkCompleted();
                _pendingRemoval.Add(task);
            }
            else
            {
                // Preserve sub-frame remainder so high-frequency recurring tasks don't drift.
                task.DelaySeconds += task.IntervalSeconds;
            }
        }

        if (_pendingRemoval.Count > 0)
        {
            foreach (var t in _pendingRemoval) _tasks.Remove(t);
            _pendingRemoval.Clear();
        }
    }

    private static void InvokeSafely(ScheduledTask task)
    {
        try
        {
            task.Callback();
        }
        catch (Exception ex)
        {
            var owner = string.IsNullOrEmpty(task.OwnerId) ? "<anonymous>" : task.OwnerId;
            MelonLogger.Error($"Scheduler: callback from '{owner}' threw: {ex}");
        }
    }

    private static void ValidateCooldownKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Cooldown key must be non-empty.", nameof(key));

        if (ModNamespace.IsValid(key) && !ModNamespace.IsReserved(key)) return;

        throw new ArgumentException(
            $"Cooldown key '{key}' must be a non-reserved dotted-lowercase namespace prefixed by your mod id (e.g. \"mymod.ability\").",
            nameof(key));
    }
}
