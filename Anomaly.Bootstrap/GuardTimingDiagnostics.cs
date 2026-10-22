using System.Diagnostics;
using System.Globalization;
using System.Text;
using MelonLoader;

namespace Anomaly.Bootstrap;

// Opt-in observations only. No sampling, public API, guard admission decision,
// audit frequency, or protection scope depends on these diagnostic values.
internal enum GuardTimingPhase
{
    CoreResolve, CoreVerify, CoreLogDiagnostics, VerifyResolve, VerifyDeferred,
    VerifyCoverage, VerifyHooks, VerifyStockHooks, VerifyCallbacks, AuditPreexisting,
    AuditHarmony, AuditManagedDetours, AuditIlHooks, CatalogResolveLoaded, CatalogCandidateClassification,
    GuardSlice, Checkpoint
}

internal enum GuardTimingCount
{
    RequiredHooks, StockHooks, Callbacks, PatchedMethods, ManagedMapEntries,
    ManagedDetours, IlHookMapEntries, IlHooks
}

internal enum GuardTimingTotal
{
    ResolverCalls, ResolverAssemblies, CandidateClassifications, Checkpoints, CheckpointReuses,
    CompletedChecks, SliceItems, ItemOverruns, PatchRecords, ManagedDetourRecords, IlHookRecords,
    CallbackChecks, TargetAudits
}

internal static class GuardTimingDiagnostics
{
    internal static readonly bool Enabled = ReadEnabled();
    private static readonly bool FrameCorrelationEnabled = Enabled && ReadFrameCorrelationEnabled();
    private const int VerifyWindowCapacity = 8192;
    private static readonly VerifyWindow[] VerifyWindows = FrameCorrelationEnabled
        ? new VerifyWindow[VerifyWindowCapacity] : Array.Empty<VerifyWindow>();
    private static int _verifyWindowCount;
    private static int _verifyWindowDropped;
    private readonly struct VerifyWindow
    {
        internal readonly long Start, End;
        internal readonly int Thread;
        internal VerifyWindow(long start, long end)
        { Start = start; End = end; Thread = Environment.CurrentManagedThreadId; }
    }
    private static readonly object Gate = new();
    private static readonly Aggregate[] Phases = new Aggregate[Enum.GetValues<GuardTimingPhase>().Length];
    private static readonly Aggregate[] Counts = new Aggregate[8];
    private static readonly long[] Totals = new long[Enum.GetValues<GuardTimingTotal>().Length];
    [ThreadStatic] private static VerificationCounts _verification;
    private static long _windowStart;
    private static long _nextReport;

    internal readonly struct Scope : IDisposable
    {
        private readonly GuardTimingPhase _phase;
        private readonly long _start;
        internal Scope(GuardTimingPhase phase, long start) { _phase = phase; _start = start; }
        public void Dispose()
        {
            if (_start == 0) return;
            try
            {
                long ended = Stopwatch.GetTimestamp();
                long elapsed = ended - _start;
                lock (Gate)
                {
                    Phases[(int)_phase].Add(elapsed);
                    if (FrameCorrelationEnabled && _phase is GuardTimingPhase.GuardSlice or GuardTimingPhase.Checkpoint)
                    {
                        if (_verifyWindowCount < VerifyWindows.Length)
                            VerifyWindows[_verifyWindowCount++] = new VerifyWindow(_start, ended);
                        else ++_verifyWindowDropped;
                    }
                }
            }
            catch { /* Optional diagnostics must never affect guard control flow. */ }
        }
    }

    private struct Aggregate
    {
        internal long Samples, Total, Minimum, Maximum;
        internal void Add(long value)
        {
            if (Samples == 0) Minimum = Maximum = value;
            else { Minimum = Math.Min(Minimum, value); Maximum = Math.Max(Maximum, value); }
            ++Samples;
            Total += value;
        }
    }

    private struct VerificationCounts
    {
        internal long RequiredHooks, StockHooks, Callbacks, PatchedMethods;
        internal long ManagedMapEntries, ManagedDetours, IlHookMapEntries, IlHooks;
    }

    internal static Scope Begin(GuardTimingPhase phase)
    {
        if (!Enabled) return default;
        try { return new Scope(phase, Stopwatch.GetTimestamp()); }
        catch { return default; }
    }

    internal static void BeginVerification()
    {
        if (Enabled) _verification = default;
    }

    internal static void CountTotal(GuardTimingTotal counter)
    {
        if (Enabled) Interlocked.Increment(ref Totals[(int)counter]);
    }

    internal static void RecordSlice(int items, int overruns)
    {
        if (!Enabled) return;
        Interlocked.Add(ref Totals[(int)GuardTimingTotal.SliceItems], items);
        Interlocked.Add(ref Totals[(int)GuardTimingTotal.ItemOverruns], overruns);
    }

    internal static void Count(GuardTimingCount counter)
    {
        if (!Enabled) return;
        switch (counter)
        {
            case GuardTimingCount.RequiredHooks: ++_verification.RequiredHooks; break;
            case GuardTimingCount.StockHooks: ++_verification.StockHooks; break;
            case GuardTimingCount.Callbacks: ++_verification.Callbacks; break;
            case GuardTimingCount.PatchedMethods: ++_verification.PatchedMethods; break;
            case GuardTimingCount.ManagedMapEntries: ++_verification.ManagedMapEntries; break;
            case GuardTimingCount.ManagedDetours: ++_verification.ManagedDetours; break;
            case GuardTimingCount.IlHookMapEntries: ++_verification.IlHookMapEntries; break;
            case GuardTimingCount.IlHooks: ++_verification.IlHooks; break;
        }
    }

    internal static void EndVerification()
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Counts[0].Add(_verification.RequiredHooks);
                Counts[1].Add(_verification.StockHooks);
                Counts[2].Add(_verification.Callbacks);
                Counts[3].Add(_verification.PatchedMethods);
                Counts[4].Add(_verification.ManagedMapEntries);
                Counts[5].Add(_verification.ManagedDetours);
                Counts[6].Add(_verification.IlHookMapEntries);
                Counts[7].Add(_verification.IlHooks);
            }
        }
        catch { /* Counts do not participate in security decisions. */ }
    }

    internal static void ReportIfDue()
    {
        if (!Enabled) return;
        try
        {
            long now = Stopwatch.GetTimestamp();
            string report;
            string? correlationReport = null;
            lock (Gate)
            {
                if (_nextReport == 0)
                {
                    _verifyWindowCount = _verifyWindowDropped = 0;
                    // Startup verification can precede the first periodic update.
                    // Discard it so every reported aggregate covers this QPC window.
                    Array.Clear(Phases, 0, Phases.Length);
                    Array.Clear(Counts, 0, Counts.Length);
                    for (int i = 0; i < Totals.Length; ++i) Interlocked.Exchange(ref Totals[i], 0);
                    _windowStart = now;
                    _nextReport = now + 30L * Stopwatch.Frequency;
                    return;
                }
                if (now < _nextReport) return;
                var builder = new StringBuilder(2048);
                builder.Append("BOOTSTRAP_TIMING windowStartQpc=").Append(_windowStart)
                    .Append(" windowEndQpc=").Append(now).Append(" qpcFrequency=").Append(Stopwatch.Frequency)
                    .Append(" enabledBy=SLVR_BOOTSTRAP_TIMING phases=count/avgMs/minMs/maxMs:");
                for (int i = 0; i < Phases.Length; ++i)
                {
                    Aggregate value = Phases[i];
                    if (value.Samples == 0) continue;
                    double milliseconds = 1000d / Stopwatch.Frequency;
                    builder.Append(' ').Append((GuardTimingPhase)i).Append('=')
                        .Append(value.Samples).Append('/')
                        .Append((value.Total * milliseconds / value.Samples).ToString("F4", CultureInfo.InvariantCulture)).Append('/')
                        .Append((value.Minimum * milliseconds).ToString("F4", CultureInfo.InvariantCulture)).Append('/')
                        .Append((value.Maximum * milliseconds).ToString("F4", CultureInfo.InvariantCulture));
                }
                builder.Append(" inspectedPerVerify=count/avg/min/max:");
                for (int i = 0; i < Counts.Length; ++i)
                {
                    Aggregate value = Counts[i];
                    if (value.Samples == 0) continue;
                    builder.Append(' ').Append((GuardTimingCount)i).Append('=')
                        .Append(value.Samples).Append('/')
                        .Append((value.Total / (double)value.Samples).ToString("F2", CultureInfo.InvariantCulture)).Append('/')
                        .Append(value.Minimum).Append('/').Append(value.Maximum);
                }
                builder.Append(" totals:");
                for (int i = 0; i < Totals.Length; ++i)
                    builder.Append(' ').Append((GuardTimingTotal)i).Append('=')
                        .Append(Interlocked.Exchange(ref Totals[i], 0));
                report = builder.ToString();
                if (FrameCorrelationEnabled)
                {
                    var windows = new StringBuilder(3072);
                    windows.Append("BOOTSTRAP_VERIFY_QPC windowStartQpc=").Append(_windowStart)
                        .Append(" windowEndQpc=").Append(now).Append(" qpcFrequency=").Append(Stopwatch.Frequency)
                        .Append(" dropped=").Append(_verifyWindowDropped).Append(" windows=");
                    for (int i = 0; i < _verifyWindowCount; ++i)
                    {
                        VerifyWindow window = VerifyWindows[i];
                        if (i != 0) windows.Append(',');
                        windows.Append(window.Start).Append(':').Append(window.End).Append(':').Append(window.Thread);
                    }
                    correlationReport = windows.ToString();
                    Array.Clear(VerifyWindows, 0, _verifyWindowCount);
                    _verifyWindowCount = _verifyWindowDropped = 0;
                }
                Array.Clear(Phases, 0, Phases.Length);
                Array.Clear(Counts, 0, Counts.Length);
                _windowStart = now;
                _nextReport = now + 30L * Stopwatch.Frequency;
            }
            MelonLogger.Msg(report);
            if (correlationReport != null) MelonLogger.Msg(correlationReport);
        }
        catch { /* Even diagnostic output failure must preserve guard behavior. */ }
    }

    private static bool ReadFrameCorrelationEnabled()
    {
        try { return string.Equals(Environment.GetEnvironmentVariable("SLVR_BOOTSTRAP_FRAME_CORRELATION"), "1", StringComparison.Ordinal); }
        catch { return false; }
    }

    private static bool ReadEnabled()
    {
        try { return string.Equals(Environment.GetEnvironmentVariable("SLVR_BOOTSTRAP_TIMING"), "1", StringComparison.Ordinal); }
        catch { return false; }
    }
}
