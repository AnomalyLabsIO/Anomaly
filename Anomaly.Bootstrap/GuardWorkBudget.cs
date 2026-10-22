using System.Diagnostics;

namespace Anomaly.Bootstrap;

// Cooperative: a runtime call already in progress cannot be preempted.
internal sealed class GuardWorkBudget
{
    internal const int MaximumItems = 32;
    private readonly Func<long> _clock;
    private readonly long _start;
    private readonly long _limit;
    private long _itemStart;
    internal int Items { get; private set; }
    internal int Overruns { get; private set; }

    internal GuardWorkBudget() : this(Stopwatch.GetTimestamp, Math.Max(1, Stopwatch.Frequency / 1000)) { }
    internal GuardWorkBudget(Func<long> clock, long limit)
    {
        _clock = clock;
        _limit = limit;
        _start = _itemStart = clock();
    }

    internal bool CanStartItem()
    {
        _itemStart = _clock();
        return Items < MaximumItems && _itemStart - _start < _limit;
    }

    internal void CompleteItem()
    {
        ++Items;
        if (_clock() - _itemStart > _limit) ++Overruns;
    }
}
