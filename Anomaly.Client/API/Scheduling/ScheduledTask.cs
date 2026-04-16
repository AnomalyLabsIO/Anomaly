using System;

namespace Anomaly.Client.API.Scheduling;

/// <summary>
/// Handle for a task registered with <see cref="Scheduler"/>. Use <see cref="Cancel"/>
/// to prevent future invocations. Canceling an already-finished or already-canceled
/// task is a no-op.
/// </summary>
public sealed class ScheduledTask
{
    internal enum TaskKind
    {
        OneShot,
        Recurring
    }

    internal TaskKind Kind { get; }
    internal double DelaySeconds { get; set; }       // remaining delay until next fire
    internal double IntervalSeconds { get; }         // recurring only; 0 for one-shot
    internal Action Callback { get; }
    internal string OwnerId { get; }                 // plugin id for diagnostics
    internal bool Paused { get; set; }

    private bool _canceled;
    private bool _completed;

    internal ScheduledTask(TaskKind kind, double delaySeconds, double intervalSeconds,
        Action callback, string ownerId)
    {
        Kind = kind;
        DelaySeconds = delaySeconds;
        IntervalSeconds = intervalSeconds;
        Callback = callback;
        OwnerId = ownerId ?? string.Empty;
    }

    /// <summary>True while the task is still eligible to fire.</summary>
    public bool IsActive => !_canceled && !_completed;

    /// <summary>
    /// Seconds until next fire, or 0 if the task is not active. Paused tasks
    /// report their frozen remaining time.
    /// </summary>
    public double RemainingSeconds => IsActive ? Math.Max(0, DelaySeconds) : 0;

    /// <summary>Cancels future invocations. Safe to call multiple times.</summary>
    public void Cancel() => _canceled = true;

    internal bool IsCanceled => _canceled;
    internal void MarkCompleted() => _completed = true;
}
