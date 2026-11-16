using System;
using System.Collections.Generic;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Server.FileTransfer;

/// <summary>Admission-only estimate of the existing main-thread pump's service capacity.</summary>
internal sealed class BulkSchedulerCapacity
{
    private const int MaximumServiceSamples = 128;
    private const int MaximumTickSamples = 64;
    private const int MinimumServiceSamples = 32;
    private const int MinimumCadenceSamples = 8;
    private const double ForecastHeadroom = .9;
    private readonly Queue<ServiceSample> _service = new Queue<ServiceSample>();
    private readonly Queue<TickSample> _ticks = new Queue<TickSample>();
    private double _serviceSeconds, _serviceCharge, _cadenceSeconds, _otherSeconds, _saturatedCharge;
    private int _cadences, _saturatedTicks;
    private double _lastStarted;
    private bool _hasLastStarted;

    internal void ObserveSend(int payloadBytes, int accountedBytes, double seconds)
    {
        // Tiny-file framing cost is not a useful bytes/crypto calibration for
        // large chunks. Its transaction rate is bounded separately by the file
        // completion pacer; do not retain a tiny-frame byte rate as a host cap.
        if (payloadBytes < FileChunkMessage.MaxChunkBytes / 2 || accountedBytes <= 0 || !Finite(seconds) || seconds < 0) return;
        var sample = new ServiceSample { Seconds = seconds, Charge = accountedBytes };
        _service.Enqueue(sample); _serviceSeconds += seconds; _serviceCharge += accountedBytes;
        if (_service.Count <= MaximumServiceSamples) return;
        sample = _service.Dequeue(); _serviceSeconds -= sample.Seconds; _serviceCharge -= sample.Charge;
    }

    internal void ObserveTick(double started, double finished, double successfulSendSeconds,
        long accountedBytes, bool allReadyTimeLimited)
    {
        double cadence = _hasLastStarted ? started - _lastStarted : 0;
        _lastStarted = started; _hasLastStarted = true;
        if (!Finite(cadence) || cadence <= 0) cadence = 0;
        double other = Math.Max(0, finished - started - successfulSendSeconds);
        if (!Finite(other)) return;
        var sample = new TickSample { Cadence = cadence, OtherSeconds = other,
            SaturatedCharge = allReadyTimeLimited ? accountedBytes : 0 };
        _ticks.Enqueue(sample); Add(sample, 1);
        if (_ticks.Count <= MaximumTickSamples) return;
        Add(_ticks.Dequeue(), -1);
    }

    internal void Inactive()
    {
        // Retain successful crypto service cost, but an idle host has no
        // current polling/cadence workload. A zero estimate from a retired
        // source must not reject every new request before it can requalify.
        _hasLastStarted = false;
        _ticks.Clear();
        _cadenceSeconds = _otherSeconds = _saturatedCharge = 0;
        _cadences = _saturatedTicks = 0;
    }

    internal void Reset()
    {
        _service.Clear(); _ticks.Clear();
        _serviceSeconds = _serviceCharge = _cadenceSeconds = _otherSeconds = _saturatedCharge = 0;
        _cadences = _saturatedTicks = 0; _hasLastStarted = false;
    }

    internal Estimate GetEstimate(BulkTransferBudget budget)
    {
        double cadence = _cadences == 0 ? 0 : _cadenceSeconds / _cadences;
        double other = _ticks.Count == 0 ? 0 : _otherSeconds / _ticks.Count;
        double meanService = _service.Count == 0 ? 0 : _serviceSeconds / _service.Count;
        double fallback = Math.Min(budget.GlobalBytesPerSecond,
            Math.Min(budget.ClientBytesPerSecond, BulkTransferBudget.Default.ClientBytesPerSecond));
        bool measured = _service.Count >= MinimumServiceSamples && _serviceSeconds > 0 &&
            _serviceCharge > 0 && _cadences >= MinimumCadenceSamples && cadence > 0;
        string reason = _service.Count < MinimumServiceSamples || _serviceSeconds <= 0
            ? "unmeasured-service-cost" : "unmeasured-update-cadence";
        double rate = fallback;
        if (measured)
        {
            double charge = BulkTransferBudget.Charge(FileChunkMessage.MaxChunkBytes);
            double fullChunkSeconds = _serviceSeconds / _serviceCharge * charge;
            double available = Math.Max(0, BulkTransferPump.MaximumSecondsPerUpdate - other);
            // The existing loop checks between operations: a final operation
            // may cross the boundary. Model that discrete behavior without
            // changing or silently enlarging the pump's time allowance.
            double operations = available <= 0 ? 0 : Math.Ceiling(available / fullChunkSeconds - 1e-9);
            operations = Math.Min(BulkTransferPump.MaximumFramesPerUpdate, operations);
            rate = operations * charge / cadence;
            rate = Math.Min(rate, BulkTransferBudget.TransferCharge(BulkTransferPump.MaximumBytesPerUpdate) / cadence);
            reason = "measured-ready-service-cost";
            if (_saturatedTicks >= 4)
            {
                // Only ticks stopped by CPU with every attempted source ready
                // contribute here. ACK/credit/storage waits cannot lower this
                // corroborating CPU-capacity sample via elapsed goodput.
                rate = Math.Min(rate, _saturatedCharge / _saturatedTicks / cadence);
                reason = "measured-ready-time-limit";
            }
            rate = Math.Min(budget.GlobalBytesPerSecond, Math.Max(0, rate * ForecastHeadroom));
        }
        return new Estimate(rate, measured, reason, _service.Count, _cadences, _saturatedTicks,
            meanService * 1000000, other * 1000, cadence * 1000);
    }

    private void Add(TickSample sample, int sign)
    {
        _otherSeconds += sign * sample.OtherSeconds;
        if (sample.Cadence > 0) { _cadenceSeconds += sign * sample.Cadence; _cadences += sign; }
        if (sample.SaturatedCharge > 0) { _saturatedCharge += sign * sample.SaturatedCharge; _saturatedTicks += sign; }
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private struct ServiceSample { internal double Seconds, Charge; }
    private struct TickSample { internal double Cadence, OtherSeconds; internal long SaturatedCharge; }

    internal readonly struct Estimate
    {
        internal Estimate(double rate, bool measured, string reason, int service, int cadence, int saturated,
            double serviceMicroseconds, double otherMilliseconds, double cadenceMilliseconds)
        {
            AccountedBytesPerSecond = rate; IsMeasured = measured; Reason = reason;
            ServiceSamples = service; CadenceSamples = cadence; SaturatedTicks = saturated;
            SuccessfulSendMicroseconds = serviceMicroseconds; OtherMillisecondsPerTick = otherMilliseconds;
            CadenceMilliseconds = cadenceMilliseconds;
        }
        public double AccountedBytesPerSecond { get; }
        public bool IsMeasured { get; }
        public string Reason { get; }
        public int ServiceSamples { get; }
        public int CadenceSamples { get; }
        public int SaturatedTicks { get; }
        public double SuccessfulSendMicroseconds { get; }
        public double OtherMillisecondsPerTick { get; }
        public double CadenceMilliseconds { get; }
    }
}
