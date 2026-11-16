using System;
using System.Diagnostics;
using Anomaly.Shared.Networking;

namespace Anomaly.Server.FileTransfer;

/// <summary>Bounds completion traffic and the file ACKs it authorizes, including retries.</summary>
internal sealed class FileCompletionPacer
{
    internal const int MessagesPerSecond = 128;
    internal const int Burst = BulkTransferProtocol.MaxFilesInFlight;
    private readonly Func<double> _clock;
    private double _tokens = Burst;
    private double _lastRefill;

    internal FileCompletionPacer(Func<double> clock = null)
    {
        _clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        _lastRefill = _clock();
    }

    internal bool TryTake()
    {
        double now = _clock();
        _tokens = Math.Min(Burst, _tokens + Math.Max(0, now - _lastRefill) * MessagesPerSecond);
        _lastRefill = Math.Max(_lastRefill, now);
        if (_tokens < 1) return false;
        _tokens--;
        return true;
    }
}
