using System;
using System.Collections.Generic;
using Anomaly.Client.API.Mods;
using MelonLoader;

namespace Anomaly.Client.API.Scheduling;

/// <summary>
/// Unified client-side scheduler for delayed callbacks, recurring ticks, and
/// named cooldowns. Ticked every frame from <c>ClientRuntime.Update</c> via the
/// internal <c>SchedulerTick</c>. All timing is advanced using
/// <see cref="UnityEngine.Time.unscaledDeltaTime"/> so pauses in game time do not
/// delay scheduled work.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cooldowns</b> are addressed by a string key, which must be prefixed by a
/// registered mod id (see <see cref="AnomalyClientMods"/>). This prevents
/// accidental collisions between mods.
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
    public static ScheduledTask In(TimeSpan delay, Action callback, string ownerId)
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
    public static ScheduledTask Every(TimeSpan interval, Action callback, TimeSpan firstDelay, string ownerId)
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
    /// registered mod id.
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
    public static void PauseAll() => _paused = true;

    public static void ResumeAll() => _paused = false;

    /// <summary>
    /// Cancels all tasks and clears all cooldowns. Intended for catastrophic
    /// reset paths (disconnect, application shutdown). Individual plugins should
    /// <see cref="ScheduledTask.Cancel"/> their own handles.
    /// </summary>
    public static void ClearAll()
    {
        foreach (var t in _tasks) t.Cancel();
        _tasks.Clear();
        _cooldowns.Clear();
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
            foreach (var kvp in _cooldowns)
            {
                double remaining = kvp.Value - deltaSeconds;
                if (remaining <= 0)
                {
                    (expired ??= new List<string>()).Add(kvp.Key);
                }
                else
                {
                    _cooldowns[kvp.Key] = remaining;
                }
            }
            if (expired != null)
                foreach (var key in expired) _cooldowns.Remove(key);
        }

        // Tasks: iterate snapshot-safe; allow callbacks to register new tasks without
        // affecting this frame's iteration.
        int count = _tasks.Count;
        for (int i = 0; i < count; i++)
        {
            var task = _tasks[i];
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

        // Require the key to be owned by a registered mod unless the Anomaly core
        // itself is the owner (prefix "anomaly."). This prevents accidental collisions.
        if (key.StartsWith("anomaly.", StringComparison.Ordinal)) return;
        if (AnomalyClientMods.IsKeyOwnedByRegisteredMod(key)) return;

        throw new ArgumentException(
            $"Cooldown key '{key}' must be prefixed by a registered mod id (see AnomalyClientMods.Register).",
            nameof(key));
    }
}
