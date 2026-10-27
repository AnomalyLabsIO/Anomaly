using System;
using System.Collections.Generic;
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
    private static List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
    private static Timer _debounce;
    private static int _epoch;
    public static void Start(string directory)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(directory)) return;
        string[] roots = new[] { Path.GetFullPath(directory) };
        int epoch;
        lock (Sync) epoch = _epoch;
        Task.Run(() =>
        {
            var watchers = new List<FileSystemWatcher>();
            try
            {
                foreach (string root in roots)
                {
                    Directory.CreateDirectory(root);
                    var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                    watchers.Add(watcher);
                    watcher.Changed += Changed;
                    watcher.Created += Changed;
                    watcher.Deleted += Changed;
                    watcher.Renamed += Renamed;
                    watcher.Error += (_, __) => QueueRescan();
                }
                lock (Sync)
                {
                    if (epoch != _epoch) { foreach (var watcher in watchers) watcher.Dispose(); return; }
                    _debounce = new Timer(_ => ServerAssetManager.Rescan(), null, Timeout.Infinite, Timeout.Infinite);
                    _watchers = watchers;
                    foreach (var watcher in watchers) watcher.EnableRaisingEvents = true;
                }
                // Closes the race between initial enumeration and watcher installation.
                ServerAssetManager.Rescan();
            }
            catch (Exception exception) { foreach (var watcher in watchers) watcher.Dispose(); OffThreadLogger.Warn("Content watcher failed: " + exception.Message); }
        });
    }
    public static void Stop()
    {
        List<FileSystemWatcher> watchers;
        Timer timer;
        lock (Sync) { _epoch++; watchers = _watchers; timer = _debounce; _watchers = new List<FileSystemWatcher>(); _debounce = null; }
        timer?.Dispose();
        Task.Run(() => { foreach (var watcher in watchers) watcher.Dispose(); });
    }
    private static void Changed(object sender, FileSystemEventArgs args)
    {
        string marker = Path.DirectorySeparatorChar + ".anomaly-spool";
        if (args.FullPath.EndsWith(marker, StringComparison.OrdinalIgnoreCase) || args.FullPath.IndexOf(marker + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0) return;
        // Every declared relative path may be author-chosen, including schemas and item data.
        QueueRescan();
    }
    private static void Renamed(object sender, RenamedEventArgs args) { Changed(sender, args); Changed(sender, new FileSystemEventArgs(WatcherChangeTypes.Renamed, Path.GetDirectoryName(args.OldFullPath), Path.GetFileName(args.OldFullPath))); }
    private static void QueueRescan() { lock (Sync) _debounce?.Change(250, Timeout.Infinite); }
}
