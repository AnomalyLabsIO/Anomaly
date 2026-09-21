using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Client.Api.Input;

namespace Anomaly.Client.Api.Internal.Input;

internal readonly struct InputFrameBinding
{
    internal InputFrameBinding(string id, InputKey key, object registration)
    {
        Id = id;
        Key = key;
        Registration = registration;
    }

    internal string Id { get; }
    internal InputKey Key { get; }
    internal object Registration { get; }
}

/// <summary>
/// Tracks binding edges and dispatches one immutable frame snapshot. State is
/// committed before callbacks so re-entrant polling cannot repeat an edge.
/// </summary>
internal sealed class InputFrameDispatcher
{
    private readonly Dictionary<object, bool> _previous = new();

    internal void Update(
        IEnumerable<InputFrameBinding> bindings,
        Func<InputKey, bool> isHeld,
        Func<InputFrameBinding, bool> isCurrent,
        Action<InputFrameBinding> pressed,
        Action<InputFrameBinding> released)
    {
        if (bindings == null) throw new ArgumentNullException(nameof(bindings));
        if (isHeld == null) throw new ArgumentNullException(nameof(isHeld));
        if (isCurrent == null) throw new ArgumentNullException(nameof(isCurrent));
        if (pressed == null) throw new ArgumentNullException(nameof(pressed));
        if (released == null) throw new ArgumentNullException(nameof(released));

        InputFrameBinding[] snapshot = bindings.ToArray();
        foreach (InputFrameBinding binding in snapshot)
        {
            if (!isCurrent(binding)) continue;
            bool held = isHeld(binding.Key);
            if (!isCurrent(binding)) continue;
            _previous.TryGetValue(binding.Registration, out bool wasHeld);

            // Commit before invoking user callbacks. A nested Update observes
            // the new state and cannot emit this transition twice.
            _previous[binding.Registration] = held;

            if (held && !wasHeld)
                pressed(binding);
            else if (!held && wasHeld)
                released(binding);
        }
    }

    internal void Reset() => _previous.Clear();

    internal void Forget(object registration) => _previous.Remove(registration);
}
