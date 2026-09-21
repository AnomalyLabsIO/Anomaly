using System;

namespace Anomaly.Client.Api.Internal;

internal readonly struct CleanupStep
{
    internal CleanupStep(string name, Action action)
    {
        Name = name ?? string.Empty;
        Action = action ?? throw new ArgumentNullException(nameof(action));
    }

    internal string Name { get; }
    internal Action Action { get; }
}

internal static class IsolatedCleanup
{
    internal static void Run(Action<string, Exception> log, params CleanupStep[] steps)
    {
        if (steps == null)
            return;

        foreach (CleanupStep step in steps)
        {
            try
            {
                step.Action();
            }
            catch (Exception exception)
            {
                try { log?.Invoke(step.Name, exception); }
                catch { /* cleanup and later steps must not depend on logging */ }
            }
        }
    }
}
