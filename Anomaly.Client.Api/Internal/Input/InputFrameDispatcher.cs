using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Client.Api.Input;

namespace Anomaly.Client.Api.Internal.Input;

/// <summary>
/// Tracks binding edges and dispatches one immutable frame snapshot. State is
/// committed before callbacks so re-entrant polling cannot repeat an edge.
/// </summary>
internal sealed class InputFrameDispatcher
{
    private readonly Dictionary<string, bool> _previous = new(StringComparer.Ordinal);

    internal void Update(
        IEnumerable<KeyValuePair<string, InputKey>> bindings,
        Func<InputKey, bool> isHeld,
        Action<string> pressed,
        Action<string> released)
    {
        if (bindings == null) throw new ArgumentNullException(nameof(bindings));
        if (isHeld == null) throw new ArgumentNullException(nameof(isHeld));
        if (pressed == null) throw new ArgumentNullException(nameof(pressed));
        if (released == null) throw new ArgumentNullException(nameof(released));

        KeyValuePair<string, InputKey>[] snapshot = bindings.ToArray();
        foreach (KeyValuePair<string, InputKey> binding in snapshot)
        {
            bool held = isHeld(binding.Value);
            _previous.TryGetValue(binding.Key, out bool wasHeld);

            // Commit before invoking user callbacks. A nested Update observes
            // the new state and cannot emit this transition twice.
            _previous[binding.Key] = held;

            if (held && !wasHeld)
                pressed(binding.Key);
            else if (!held && wasHeld)
                released(binding.Key);
        }
    }

    internal void Reset() => _previous.Clear();
}
