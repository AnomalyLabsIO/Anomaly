using System;
using System.Collections.Generic;
using MelonLoader;

namespace Anomaly.Client.API.Commands;

/// <summary>
/// Per-invocation context passed to <see cref="ICommand.Execute"/>. Carries the
/// parsed argument list and an output sink for replies.
/// </summary>
public sealed class CommandContext
{
    private readonly Action<string> _replySink;

    /// <summary>The full invocation line, minus the command name.</summary>
    public string RawArgs { get; }

    /// <summary>Parsed, whitespace-split arguments. Quoted segments are preserved.</summary>
    public IReadOnlyList<string> Args { get; }

    public CommandContext(string rawArgs, IReadOnlyList<string> args, Action<string> replySink)
    {
        RawArgs = rawArgs ?? string.Empty;
        Args = args ?? (IReadOnlyList<string>)new List<string>();
        _replySink = replySink ?? (s => MelonLogger.Msg(s));
    }

    /// <summary>Writes a line of output back to the invoker (typically the client console).</summary>
    public void Reply(string line) => _replySink(line ?? string.Empty);

    /// <summary>Convenience: returns Args[i] or <paramref name="fallback"/> if out of range.</summary>
    public string ArgOr(int i, string fallback = "") =>
        i >= 0 && i < Args.Count ? Args[i] : fallback;

    /// <summary>Convenience: tries to parse Args[i] as an integer.</summary>
    public bool TryArgInt(int i, out int value)
    {
        value = 0;
        return i >= 0 && i < Args.Count && int.TryParse(Args[i], out value);
    }
}
