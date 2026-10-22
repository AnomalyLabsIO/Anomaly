using System;
using System.Collections.Generic;

namespace Anomaly.Server.Poses;

/// <summary>A rolling one-second recipient ceiling, shared by reliable control, cosmetic samples and combat samples.</summary>
internal sealed class PoseTrafficBudget
{
    internal const int MaximumPacketsPerSecond = 512;
    internal const int MaximumBytesPerSecond = 192 * 1024;
    internal const int ReservedControlPackets = 16;
    internal const int ReservedControlBytes = 16 * 1024;
    private readonly Queue<Charge> _charges = new Queue<Charge>();
    private int _packets;
    private int _bytes;
    private int _samplePackets;
    private int _sampleBytes;
    // Independent byte reservations prevent either authority starving the other. A
    // two-tick token capacity smooths the old rolling-window burst-and-pause behavior.
    // Eight KiB of sample headroom absorbs whole-frame rounding at the rolling-window edge.
    internal const int CosmeticBytesPerSecond = 116 * 1024;
    internal const int CombatBytesPerSecond = 52 * 1024;
    private double _cosmeticCredit = CosmeticBytesPerSecond * .05;
    private double _combatCredit = CombatBytesPerSecond * .05;
    private double _cosmeticRefill = double.NaN;
    private double _combatRefill = double.NaN;
    private readonly struct Charge
    {
        internal Charge(double time, int packets, int bytes, bool sample) { Time = time; Packets = packets; Bytes = bytes; Sample = sample; }
        internal double Time { get; }
        internal int Packets { get; }
        internal int Bytes { get; }
        internal bool Sample { get; }
    }

    internal bool TryCharge(double now, int packets, int bytes) => TryCharge(now, packets, bytes, false);
    internal bool TryChargeSamples(double now, int packets, int bytes, bool combat = false)
    {
        if (double.IsNaN(now) || double.IsInfinity(now)) return false;
        double rate = combat ? CombatBytesPerSecond : CosmeticBytesPerSecond;
        ref double credit = ref (combat ? ref _combatCredit : ref _cosmeticCredit);
        ref double last = ref (combat ? ref _combatRefill : ref _cosmeticRefill);
        if (!double.IsNaN(last) && now < last) return false;
        if (!double.IsNaN(last)) credit = Math.Min(rate * .10, credit + (now - last) * rate);
        last = now;
        if (bytes > credit || !TryCharge(now, packets, bytes, true)) return false;
        credit -= bytes;
        return true;
    }

    private bool TryCharge(double now, int packets, int bytes, bool sample)
    {
        while (_charges.Count > 0 && now - _charges.Peek().Time >= 1d)
        {
            Charge old = _charges.Dequeue();
            _packets -= old.Packets;
            _bytes -= old.Bytes;
            if (old.Sample) { _samplePackets -= old.Packets; _sampleBytes -= old.Bytes; }
        }
        if (packets <= 0 || bytes <= 0 || packets > MaximumPacketsPerSecond - _packets || bytes > MaximumBytesPerSecond - _bytes)
            return false;
        if (sample && (packets > MaximumPacketsPerSecond - ReservedControlPackets - _samplePackets ||
            bytes > MaximumBytesPerSecond - ReservedControlBytes - _sampleBytes)) return false;
        _charges.Enqueue(new Charge(now, packets, bytes, sample));
        _packets += packets;
        _bytes += bytes;
        if (sample) { _samplePackets += packets; _sampleBytes += bytes; }
        return true;
    }
}
