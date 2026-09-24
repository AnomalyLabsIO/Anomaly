using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Utils;

namespace Anomaly.Server.Assets;

/// <summary>Coalesces changes on workers; active clients retain their original spool generation.</summary>
internal static class AssetFileWatcher
{
    private static readonly object Sync = new object();
    private static FileSystemWatcher _watcher;
    private static Timer _debounce;
    private static int _epoch;
    public static void Start(string directory)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(directory)) return;
        int epoch;
        lock (Sync) epoch = _epoch;
        Task.Run(() =>
        {
            FileSystemWatcher watcher = null;
            try
            {
                Directory.CreateDirectory(directory);
                watcher = new FileSystemWatcher(directory) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                watcher.Changed += Changed;
                watcher.Created += Changed;
                watcher.Deleted += Changed;
                watcher.Renamed += Renamed;
                watcher.Error += (_, __) => QueueRescan();
                lock (Sync)
                {
                    if (epoch != _epoch) { watcher.Dispose(); return; }
                    _debounce = new Timer(_ => ServerAssetManager.Rescan(), null, Timeout.Infinite, Timeout.Infinite);
                    _watcher = watcher;
                    watcher.EnableRaisingEvents = true;
                }
                // Closes the race between initial enumeration and watcher installation.
                ServerAssetManager.Rescan();
            }
            catch (Exception exception) { watcher?.Dispose(); OffThreadLogger.Warn("Asset watcher failed: " + exception.Message); }
        });
    }
    public static void Stop()
    {
        FileSystemWatcher watcher;
        Timer timer;
        lock (Sync) { _epoch++; watcher = _watcher; timer = _debounce; _watcher = null; _debounce = null; }
        timer?.Dispose();
        if (watcher != null) Task.Run(() => watcher.Dispose());
    }
    private static void Changed(object sender, FileSystemEventArgs args)
    {
        string marker = Path.DirectorySeparatorChar + ".anomaly-spool";
        if (args.FullPath.EndsWith(marker, StringComparison.OrdinalIgnoreCase) || args.FullPath.IndexOf(marker + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0) return;
        if (args.ChangeType != WatcherChangeTypes.Changed || args.FullPath.EndsWith(".index.json", StringComparison.OrdinalIgnoreCase) || args.FullPath.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)) QueueRescan();
    }
    private static void Renamed(object sender, RenamedEventArgs args) { Changed(sender, args); Changed(sender, new FileSystemEventArgs(WatcherChangeTypes.Renamed, Path.GetDirectoryName(args.OldFullPath), Path.GetFileName(args.OldFullPath))); }
    private static void QueueRescan() { lock (Sync) _debounce?.Change(250, Timeout.Infinite); }
}
