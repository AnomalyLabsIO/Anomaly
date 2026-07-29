using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using LabApi.Features.Console;
using MEC;

namespace Anomaly.Server.Utils;

public class OffThreadLogger
{
    private static ConcurrentQueue<LogEntry> _queue = new ConcurrentQueue<LogEntry>();
    private static bool _initialized;
    private static CoroutineHandle _drainHandle;

    /// <summary>
    /// Must be called once from the main Unity thread (e.g. Plugin.Enable) to start the drain coroutine.
    /// </summary>
    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;
        _drainHandle = Timing.RunCoroutine(DrainQueue());
    }

    public static void Disable()
    {
        if (_drainHandle.IsRunning)
            Timing.KillCoroutines(_drainHandle);

        _drainHandle = default;
        _initialized = false;
        while (_queue.TryDequeue(out _)) { }
    }

    public static void Debug(string message)
    {
        _queue.Enqueue(new LogEntry { Message = message, Color = ConsoleColor.Gray });
    }

    public static void Info(string message)
    {
        _queue.Enqueue(new LogEntry { Message = message, Color = ConsoleColor.Green });
    }

    public static void Error(string message)
    {
        _queue.Enqueue(new LogEntry { Message = message, Color = ConsoleColor.DarkRed });
    }

    public static void Warn(string message)
    {
        _queue.Enqueue(new LogEntry { Message = message, Color = ConsoleColor.Yellow });
    }

    private static IEnumerator<float> DrainQueue()
    {
        while (true)
        {
            while (_queue.TryDequeue(out var entry))
            {
                try
                {
                    Logger.Raw(entry.Message, entry.Color);
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine($"[OffThreadLogger] Failed to log: {entry.Message} - {ex.Message}");
                }
            }
            yield return Timing.WaitForOneFrame;
        }
    }

    private struct LogEntry
    {
        public string Message;
        public ConsoleColor Color;
    }
}
