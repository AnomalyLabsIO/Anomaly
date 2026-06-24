using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Server.Utils;
using Anomaly.Shared.Networking.Messages;
using MEC;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Assets
{
    /// <summary>
    /// Watches the server's asset directory for changes and pushes
    /// <see cref="AssetReloadMessage"/> broadcasts to all connected clients when
    /// a file's content actually changes. Changes are debounced to coalesce
    /// rapid editor saves.
    /// </summary>
    public static class AssetFileWatcher
    {
        private static FileSystemWatcher _watcher;
        private static readonly Dictionary<string, CoroutineHandle> _pendingDebounces = new Dictionary<string, CoroutineHandle>();

        // FileSystemWatcher callbacks fire on threadpool worker threads. MEC's Timing and
        // the _pendingDebounces dictionary are main-thread-only, so worker threads only
        // enqueue paths here; a main-thread MEC coroutine (DrainIncoming) drains them and
        // performs all debounce/scheduling work. Mirrors the OffThreadLogger pattern.
        private static readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
        private static CoroutineHandle _drainHandle;
        private const float DebounceSeconds = 0.25f;

        public static void Start(string assetDirectory)
        {
            if (string.IsNullOrEmpty(assetDirectory) || !Directory.Exists(assetDirectory))
            {
                Logger.Info("AssetFileWatcher: No asset directory, skipping watcher.");
                return;
            }

            Stop();

            _watcher = new FileSystemWatcher(assetDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileRenamed;

            _drainHandle = Timing.RunCoroutine(DrainIncoming());

            Logger.Info($"AssetFileWatcher: Watching '{assetDirectory}' for changes.");
        }

        public static void Stop()
        {
            // Called on the main thread (Start / Plugin.Disable), so MEC calls are safe.
            if (_drainHandle.IsRunning)
                Timing.KillCoroutines(_drainHandle);

            foreach (var handle in _pendingDebounces.Values)
                if (handle.IsRunning)
                    Timing.KillCoroutines(handle);
            _pendingDebounces.Clear();

            while (_incoming.TryDequeue(out _)) { }

            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        // Worker-thread entry points: only enqueue. No MEC, no dictionary access here.
        private static void OnFileEvent(object sender, FileSystemEventArgs e) => _incoming.Enqueue(e.FullPath);
        private static void OnFileRenamed(object sender, RenamedEventArgs e) => _incoming.Enqueue(e.FullPath);

        // Main-thread pump: drains worker-thread file events and runs the debounce on the
        // main thread, where Timing and _pendingDebounces are safe to touch.
        private static IEnumerator<float> DrainIncoming()
        {
            while (true)
            {
                while (_incoming.TryDequeue(out var fullPath))
                    Debounce(fullPath);
                yield return Timing.WaitForOneFrame;
            }
        }

        private static void Debounce(string fullPath)
        {
            // Runs on the main thread (via DrainIncoming) — MEC + _pendingDebounces are safe here.
            OffThreadLogger.Info($"AssetFileWatcher: change detected for '{fullPath}', debouncing.");

            if (_pendingDebounces.TryGetValue(fullPath, out var handle) && handle.IsRunning)
                Timing.KillCoroutines(handle);

            _pendingDebounces[fullPath] = Timing.CallDelayed(DebounceSeconds, () => ProcessChange(fullPath));
        }

        private static void ProcessChange(string fullPath)
        {
            _pendingDebounces.Remove(fullPath);

            if (!File.Exists(fullPath))
                return;

            byte[] newHash;
            uint newSize;
            try
            {
                var data = File.ReadAllBytes(fullPath);
                newSize = (uint)data.Length;
                using (var sha = SHA256.Create())
                    newHash = sha.ComputeHash(data);
            }
            catch (Exception ex)
            {
                Logger.Error($"AssetFileWatcher: Failed to rehash '{fullPath}': {ex.Message}");
                return;
            }

            // Find the existing registration for this full path, if any.
            byte[] oldHash = null;
            foreach (var kv in ServerAssetManager.AllAssets)
            {
                if (string.Equals(kv.Value.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    oldHash = kv.Value.Hash;
                    break;
                }
            }

            // Always rescan so the manifest stays authoritative for late-joiners.
            ServerAssetManager.Rescan();

            if (oldHash == null)
            {
                Logger.Info($"AssetFileWatcher: New asset detected '{fullPath}' (hash {Hex(newHash)}). Future clients will receive it.");
                return;
            }

            if (HashesEqual(oldHash, newHash))
            {
                // FileSystemWatcher noise (touch without content change).
                return;
            }

            Logger.Info($"AssetFileWatcher: '{fullPath}' changed: {Hex(oldHash)} → {Hex(newHash)}. Broadcasting reload.");
            AnomalyMessaging.SendToAll(new AssetReloadMessage
            {
                OldHash = oldHash,
                NewHash = newHash
            });
        }

        private static bool HashesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static string Hex(byte[] hash)
        {
            if (hash == null) return "<null>";
            return BitConverter.ToString(hash, 0, Math.Min(8, hash.Length)).Replace("-", "").ToLowerInvariant() + "…";
        }
    }
}
