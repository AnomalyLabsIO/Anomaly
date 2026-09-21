using System.Collections.Generic;

namespace Anomaly.Client.Api.Internal;

/// <summary>
/// Deduplicates a callback kind for one concrete runtime instance within one
/// frame while allowing a different instance to proceed in that same frame.
/// </summary>
internal sealed class FrameInvocationGate
{
    private readonly HashSet<long> _fired = new();
    private int _currentFrame = -1;

    internal bool ShouldFire(int instanceId, int kind, int frame)
    {
        if (frame != _currentFrame)
        {
            _fired.Clear();
            _currentFrame = frame;
        }

        long key = ((long)instanceId << 8) | (uint)(kind & 0xFF);
        return _fired.Add(key);
    }

    internal void Reset()
    {
        _fired.Clear();
        _currentFrame = -1;
    }
}
