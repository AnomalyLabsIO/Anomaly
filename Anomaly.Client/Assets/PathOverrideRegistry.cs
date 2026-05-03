using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anomaly.Client.Api.Features;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Assets
{
    /// <summary>
    /// Path-keyed asset override registry. Walks
    /// <c>UserData/Anomaly/Overrides/&lt;Kind&gt;/...</c> at init and exposes
    /// per-kind lookups that runtime hooks (e.g. CASSIE) call to substitute
    /// game assets at the engine's load point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 1 is local-only: this class scans the user's <c>Overrides/</c>
    /// directory once on <see cref="Init"/> and never re-scans (Phase 3 adds a
    /// FileSystemWatcher for hot reload). Phase 2 adds server-pushed entries
    /// from the file manifest with the same path-key shape.
    /// </para>
    /// <para>
    /// Lookup keys are lowercased forward-slash-normalized paths under the
    /// kind's subdirectory, with the file extension stripped. Examples:
    /// <list type="bullet">
    ///   <item><c>Overrides/AudioClip/ALERT.ogg</c> → kind <c>AudioClip</c>, key <c>"alert"</c></item>
    ///   <item><c>Overrides/AudioClip/SCP/096_idle.ogg</c> → kind <c>AudioClip</c>, key <c>"scp/096_idle"</c></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class PathOverrideRegistry
    {
        private static readonly Dictionary<string, string> _localAudio = new();
        private static readonly HashSet<string> _resolveLogged = new();
        private static bool _initialized;

        public static void Init()
        {
            if (_initialized)
                return;
            _initialized = true;

            ScanLocal(Paths.AudioOverridesPath, _localAudio, ".ogg", ".wav");

            int total = _localAudio.Count;
            if (total == 0)
            {
                MelonLogger.Msg($"PathOverrideRegistry: no local overrides discovered. Drop files under {Paths.OverridesPath} to add some.");
            }
            else
            {
                MelonLogger.Msg($"PathOverrideRegistry: discovered {total} local override(s) (audio={_localAudio.Count}).");
            }
        }

        public static void Shutdown()
        {
            // Local map persists between disconnects (it's tied to the user's
            // disk, not the server session); only the cached AudioClip
            // instances need to drop so the next session re-decodes from disk.
            LocalAudio.UnloadAll();
            _resolveLogged.Clear();
            // Note: _localAudio map of paths is intentionally kept; calling Init
            // again would just re-scan the same disk content.
        }

        /// <summary>
        /// Looks up an audio override by asset path. <paramref name="assetPath"/>
        /// is matched against the lowercased relative path under
        /// <c>Overrides/AudioClip/</c>; an optional leading <c>"AudioClip/"</c>
        /// segment is stripped so callers can pass either form.
        /// </summary>
        public static bool TryGetAudioClip(string assetPath, out AudioClip clip)
        {
            clip = null;
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;

            string key = NormalizeKey(assetPath, "AudioClip/");
            if (!_localAudio.TryGetValue(key, out var diskPath))
                return false;

            clip = LocalAudio.TryLoad(diskPath);
            if (clip == null)
                return false;

            if (_resolveLogged.Add("audio:" + key))
                MelonLogger.Msg($"PathOverrideRegistry: resolved AudioClip '{key}' from local ({diskPath}).");
            return true;
        }

        // --------------------------------------------------------------------

        private static void ScanLocal(string root, Dictionary<string, string> sink, params string[] extensions)
        {
            sink.Clear();
            if (!Directory.Exists(root))
                return;

            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrEmpty(ext) || !extensions.Any(e => string.Equals(ext, e, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    string rel = Path.GetRelativePath(root, file);
                    string keyWithExt = rel.Replace('\\', '/').ToLowerInvariant();
                    string key = StripExtension(keyWithExt);
                    sink[key] = file;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"PathOverrideRegistry.ScanLocal('{root}'): {ex}");
            }
        }

        private static string NormalizeKey(string assetPath, string kindPrefix)
        {
            string normalized = assetPath.Replace('\\', '/').Trim('/').ToLowerInvariant();
            string lowerPrefix = kindPrefix.ToLowerInvariant();
            if (normalized.StartsWith(lowerPrefix, StringComparison.Ordinal))
                normalized = normalized.Substring(lowerPrefix.Length);
            return StripExtension(normalized);
        }

        private static string StripExtension(string path)
        {
            int slash = path.LastIndexOf('/');
            int dot = path.LastIndexOf('.');
            if (dot > slash && dot >= 0)
                return path.Substring(0, dot);
            return path;
        }
    }
}
