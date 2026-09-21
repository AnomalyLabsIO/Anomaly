using System;
using System.Collections.Generic;

namespace Anomaly.Client.Api.Internal;

/// <summary>
/// Applies component initialization steps once and unwinds every attempted
/// step in reverse order when any step fails. Rollback actions must be safe to
/// call when their matching apply action failed before changing state.
/// </summary>
internal sealed class TransactionalInitializer
{
    private readonly List<Action> _committedRollbacks = new();

    internal bool IsInitialized { get; private set; }

    internal void Initialize(params InitializationStep[] steps)
    {
        if (IsInitialized)
            return;
        if (steps == null)
            throw new ArgumentNullException(nameof(steps));

        var attemptedRollbacks = new List<Action>(steps.Length);
        try
        {
            foreach (InitializationStep step in steps)
            {
                if (step.Apply == null || step.Rollback == null)
                    throw new ArgumentException("Initialization steps require apply and rollback actions.", nameof(steps));

                // Record rollback before applying so even a partially-applied
                // step gets a chance to restore its state.
                attemptedRollbacks.Add(step.Rollback);
                step.Apply();
            }

            _committedRollbacks.AddRange(attemptedRollbacks);
            IsInitialized = true;
        }
        catch
        {
            RollBack(attemptedRollbacks);
            throw;
        }
    }

    internal void Reset()
    {
        if (!IsInitialized && _committedRollbacks.Count == 0)
            return;

        RollBack(_committedRollbacks);
        _committedRollbacks.Clear();
        IsInitialized = false;
    }

    private static void RollBack(IReadOnlyList<Action> rollbacks)
    {
        for (int index = rollbacks.Count - 1; index >= 0; index--)
        {
            try { rollbacks[index](); }
            catch { /* continue restoring the remaining component state */ }
        }
    }
}

internal readonly struct InitializationStep
{
    internal InitializationStep(Action apply, Action rollback)
    {
        Apply = apply;
        Rollback = rollback;
    }

    internal Action Apply { get; }
    internal Action Rollback { get; }
}
