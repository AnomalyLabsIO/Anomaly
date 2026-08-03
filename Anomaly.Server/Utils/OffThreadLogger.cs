using System;
using System.Collections.Generic;
using System.Threading;
using LabApi.Features.Console;
using MEC;

namespace Anomaly.Server.Utils;

/// <summary>
/// Bounded bridge from LiteNetLib/background threads to LabAPI's main-thread logger.
/// Duplicate pending entries are coalesced and only a fixed number are drained per frame.
/// </summary>
public static class OffThreadLogger
{
    private const int MaximumQueuedEntries = 2048;
    private const int MaximumMessageCharacters = 4096;
    private const int MaximumEntriesPerFrame = 128;
    private static readonly TimeSpan DropReportInterval = TimeSpan.FromSeconds(1);

    private static readonly object Sync = new object();
    private static readonly Queue<LogEntry> Queue = new Queue<LogEntry>();
    private static readonly Dictionary<string, LogEntry> PendingByKey = new Dictionary<string, LogEntry>();
    private static bool _initialized;
    private static CoroutineHandle _drainHandle;
    private static long _droppedCount;
    private static long _coalescedCount;
    private static long _reportedDroppedCount;
    private static DateTime _nextDropReportUtc;

    public static int QueuedCount
    {
        get
        {
            lock (Sync)
                return Queue.Count;
        }
    }

    public static long DroppedCount => Interlocked.Read(ref _droppedCount);
    public static long CoalescedCount => Interlocked.Read(ref _coalescedCount);

    /// <summary>Must be called once from the main Unity thread.</summary>
    public static void Init()
    {
        if (_initialized)
            return;
        _initialized = true;
        _nextDropReportUtc = DateTime.UtcNow.Add(DropReportInterval);
        _drainHandle = Timing.RunCoroutine(DrainQueue());
    }

    public static void Disable()
    {
        if (_drainHandle.IsRunning)
            Timing.KillCoroutines(_drainHandle);

        _drainHandle = default;
        _initialized = false;
        lock (Sync)
        {
            Queue.Clear();
            PendingByKey.Clear();
        }
    }

    public static void Debug(string message) => Enqueue(message, ConsoleColor.Gray);
    public static void Info(string message) => Enqueue(message, ConsoleColor.Green);
    public static void Error(string message) => Enqueue(message, ConsoleColor.DarkRed);
    public static void Warn(string message) => Enqueue(message, ConsoleColor.Yellow);

    private static void Enqueue(string message, ConsoleColor color)
    {
        message ??= "<null>";
        if (message.Length > MaximumMessageCharacters)
            message = message.Substring(0, MaximumMessageCharacters) + " [truncated]";

        var key = ((int)color).ToString() + ":" + message;
        lock (Sync)
        {
            if (PendingByKey.TryGetValue(key, out var existing))
            {
                existing.RepeatCount++;
                Interlocked.Increment(ref _coalescedCount);
                return;
            }

            if (Queue.Count >= MaximumQueuedEntries)
            {
                Interlocked.Increment(ref _droppedCount);
                return;
            }

            var entry = new LogEntry
            {
                Key = key,
                Message = message,
                Color = color,
                RepeatCount = 1,
            };
            PendingByKey.Add(key, entry);
            Queue.Enqueue(entry);
        }
    }

    private static IEnumerator<float> DrainQueue()
    {
        while (true)
        {
            for (var i = 0; i < MaximumEntriesPerFrame; i++)
            {
                LogEntry entry;
                lock (Sync)
                {
                    if (Queue.Count == 0)
                        break;
                    entry = Queue.Dequeue();
                    PendingByKey.Remove(entry.Key);
                }

                var message = entry.RepeatCount > 1
                    ? $"{entry.Message} (repeated {entry.RepeatCount} times)"
                    : entry.Message;
                try
                {
                    Logger.Raw(message, entry.Color);
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine($"[OffThreadLogger] Failed to log an entry: {ex.Message}");
                }
            }

            ReportDropsIfDue();
            yield return Timing.WaitForOneFrame;
        }
    }

    private static void ReportDropsIfDue()
    {
        var now = DateTime.UtcNow;
        if (now < _nextDropReportUtc)
            return;
        _nextDropReportUtc = now.Add(DropReportInterval);

        var dropped = DroppedCount;
        var delta = dropped - _reportedDroppedCount;
        if (delta <= 0)
            return;
        _reportedDroppedCount = dropped;
        try
        {
            Logger.Raw(
                $"OffThreadLogger dropped {delta} entries because its bounded queue was full "
                + $"(totalDropped={dropped}, totalCoalesced={CoalescedCount}, queued={QueuedCount}).",
                ConsoleColor.Yellow);
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"[OffThreadLogger] Failed to report dropped entries: {ex.Message}");
        }
    }

    private sealed class LogEntry
    {
        public string Key;
        public string Message;
        public ConsoleColor Color;
        public int RepeatCount;
    }
}
