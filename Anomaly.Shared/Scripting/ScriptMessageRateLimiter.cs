using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Anomaly.Shared.Scripting;

/// <summary>Per-activation script traffic budgets, in addition to the outer transport limits.</summary>
internal sealed class ScriptMessageRateLimiter
{
    private sealed class Bucket
    {
        internal double Messages = 10;
        internal double Updated;
    }

    private readonly Func<double> _clock;
    private readonly Dictionary<string, Bucket> _channels = new Dictionary<string, Bucket>(StringComparer.Ordinal);
    private double _messages = 40, _bytes = 65536, _updated;

    internal ScriptMessageRateLimiter(Func<double> clock = null)
    {
        _clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        _updated = _clock();
    }

    internal bool TryAccept(string packId, string name, int bytes)
    {
        // Callers admit only known schemas. Bound keys independently as a defense
        // against accidental use before that check, without peer-driven growth.
        if (string.IsNullOrEmpty(packId) || packId.Length > 128 || string.IsNullOrEmpty(name) || name.Length > 64 ||
            bytes < 0 || bytes > ScriptMessageLimits.MaxPayloadBytes) return false;
        double now = _clock();
        if (double.IsNaN(now) || double.IsInfinity(now)) return false;
        double elapsed = Math.Max(0, now - _updated);
        _updated = Math.Max(_updated, now);
        _messages = Math.Min(40, _messages + elapsed * 20);
        _bytes = Math.Min(65536, _bytes + elapsed * 32768);
        if (_messages < 1 || _bytes < bytes) return false;
        // Attempts also consume the aggregate budget if a hot channel is denied.
        _messages--;
        _bytes -= bytes;
        string key = packId + "\0" + name;
        if (!_channels.TryGetValue(key, out var channel))
        {
            if (_channels.Count >= ScriptMessageLimits.MaxSchemasPerCatalog) return false;
            channel = new Bucket { Updated = now };
            _channels.Add(key, channel);
        }
        elapsed = Math.Max(0, now - channel.Updated);
        channel.Updated = Math.Max(channel.Updated, now);
        channel.Messages = Math.Min(10, channel.Messages + elapsed * 5);
        if (channel.Messages < 1) return false;
        channel.Messages--;
        return true;
    }
}
