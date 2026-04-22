using System;
using System.IO;
using Anomaly.Client.Api.Scheduling;
using MelonLoader;

namespace Anomaly.Client.Api.Internal.Config;

/// <summary>
/// Wraps a <see cref="FileSystemWatcher"/> and debounces change notifications
/// onto the main thread via <see cref="Scheduler.In"/>. Config files are small
/// and edits usually come in bursts (editor "save" often fires Change, Rename,
/// Create in quick succession), so we coalesce events into a single reload.
/// </summary>
internal sealed class ConfigFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _fsw;
    private readonly Action _onChange;
    private readonly string _path;
    private ScheduledTask _pending;
    private bool _suppressOnce;

    internal ConfigFileWatcher(string path, Action onChange)
    {
        _path = path;
        _onChange = onChange;

        var dir = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(fileName)) return;

        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            _fsw = new FileSystemWatcher(dir, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _fsw.Changed += OnAnyChange;
            _fsw.Created += OnAnyChange;
            _fsw.Renamed += OnRename;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ConfigFileWatcher: failed to watch '{path}': {ex.Message}");
        }
    }

    /// <summary>
    /// Skips the next change event. Call this right before the owning code
    /// writes to the file itself, so we don't bounce-reload our own writes.
    /// </summary>
    internal void SuppressNextChange() => _suppressOnce = true;

    private void OnRename(object sender, RenamedEventArgs e) => OnAnyChange(sender, e);

    private void OnAnyChange(object sender, FileSystemEventArgs e)
    {
        if (_suppressOnce)
        {
            _suppressOnce = false;
            return;
        }

        // Debounce onto the main thread: FileSystemWatcher callbacks arrive on
        // a thread-pool thread, but our config consumers expect main-thread
        // semantics. Scheduler.In runs on the main thread via ClientRuntime.Update.
        _pending?.Cancel();
        _pending = Scheduler.In(TimeSpan.FromMilliseconds(150), InvokeOnChange, ownerId: "anomaly.config");
    }

    private void InvokeOnChange()
    {
        try { _onChange?.Invoke(); }
        catch (Exception ex) { MelonLogger.Error($"ConfigFileWatcher['{_path}']: onChange threw: {ex}"); }
    }

    public void Dispose()
    {
        _pending?.Cancel();
        if (_fsw != null)
        {
            _fsw.EnableRaisingEvents = false;
            _fsw.Dispose();
        }
    }
}
