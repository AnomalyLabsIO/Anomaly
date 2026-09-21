using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Compatibility;
using Anomaly.Client.Api.Internal;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Anomaly.Client.Internal.Modding;

internal static class ManagedModRuntime
{
    private const long MaxManifestBytes = 256 * 1024;
    private const int QuarantineAfterFailures = 2;

    private sealed class Candidate
    {
        internal string AssemblyPath;
        internal string ManifestPath;
        internal ModManifest Manifest;
    }

    private sealed class LoadedModule
    {
        internal Candidate Candidate;
        internal ModManifest Manifest;
        internal IAnomalyMod Module;
        internal ModContext Context;
        internal bool Disabled;
    }

    private sealed class RuntimeState
    {
        public Dictionary<string, int> ConsecutiveStartupFailures { get; set; } =
            new(StringComparer.Ordinal);
    }

    private sealed class StartupMarker
    {
        public string ModId { get; set; }
        public string Sha256 { get; set; }
        public DateTimeOffset StartedUtc { get; set; }
    }

    private sealed class LockFile
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset ResolvedUtc { get; set; }
        public List<LockFileEntry> Modules { get; set; } = new();
    }

    private sealed class LockFileEntry
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public string Sha256 { get; set; }
    }

    private static readonly List<LoadedModule> Loaded = new();
    private static string _anomalyRoot;
    private static string _moduleRoot;
    private static string _statePath;
    private static string _markerPath;
    private static RuntimeState _state;

    internal static void Initialize()
    {
        _anomalyRoot = Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly");
        _moduleRoot = Path.Combine(_anomalyRoot, "Modules");
        _statePath = Path.Combine(_anomalyRoot, "module-health.json");
        _markerPath = Path.Combine(_anomalyRoot, "module-startup-marker.json");
        Directory.CreateDirectory(_moduleRoot);

        _state = ReadJson<RuntimeState>(_statePath, 512 * 1024) ?? new RuntimeState();
        AttributeInterruptedStartup();

        if (File.Exists(Path.Combine(_anomalyRoot, "safe-mode.flag")))
        {
            MelonLogger.Warning("Managed modules are disabled because Anomaly safe mode is active.");
            FeatureHealth.Report("anomaly", "managed-mods", FeatureHealthStatus.Disabled, "Safe mode is active.");
            return;
        }

        var candidates = DiscoverCandidates();
        var resolution = ModDependencyResolver.Resolve(candidates.Select(x => x.Manifest));
        if (!resolution.Success)
        {
            foreach (var error in resolution.Errors) MelonLogger.Error("Managed mod graph: " + error);
            FeatureHealth.Report("anomaly", "managed-mods", FeatureHealthStatus.Failed,
                "Manifest/dependency validation failed; no managed modules were loaded.");
            return;
        }

        var byId = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
            byId.Add(candidate.Manifest.Id, candidate);
        WriteJsonAtomic(Path.Combine(_anomalyRoot, "modules.lock.json"), new LockFile
        {
            ResolvedUtc = DateTimeOffset.UtcNow,
            Modules = resolution.Ordered.Select(manifest => new LockFileEntry
            {
                Id = manifest.Id,
                Version = manifest.Version,
                Sha256 = manifest.Sha256.ToLowerInvariant(),
            }).ToList(),
        });
        foreach (var manifest in resolution.Ordered)
        {
            var candidate = byId[manifest.Id];
            if (GetFailureCount(manifest.Id) >= QuarantineAfterFailures)
            {
                Quarantine(candidate, "repeated-startup-failure");
                continue;
            }
            Load(candidate);
        }

        FeatureHealth.Report("anomaly", "managed-mods", FeatureHealthStatus.Healthy,
            $"Loaded {Loaded.Count} managed module(s).");
    }

    internal static void Shutdown()
    {
        var modules = Loaded.ToArray();
        Loaded.Clear();
        for (var i = modules.Length - 1; i >= 0; i--)
        {
            var loaded = modules[i];
            if (loaded.Disabled) continue;
            loaded.Disabled = true;
            bool clean = Teardown(loaded.Manifest.Id, loaded.Module, loaded.Context);
            if (clean) _state.ConsecutiveStartupFailures.Remove(loaded.Manifest.Id);
            else IncrementFailure(loaded.Manifest.Id);
        }
        TryDelete(_markerPath);
        SaveState();
    }

    private static List<Candidate> DiscoverCandidates()
    {
        var result = new List<Candidate>();
        foreach (var assemblyPath in Directory.EnumerateFiles(_moduleRoot, "*.dll", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = assemblyPath + ".anomaly.json";
            if (!File.Exists(manifestPath))
            {
                MelonLogger.Error($"Managed mod '{Path.GetFileName(assemblyPath)}' has no sidecar manifest.");
                continue;
            }

            try
            {
                var manifest = ReadJson<ModManifest>(manifestPath, MaxManifestBytes);
                if (manifest == null) throw new InvalidDataException("Manifest was empty.");
                if (!GameVersionCompatibility.IsSupported(
                        manifest.SupportedGameVersions,
                        Il2CppGameCore.Version.Major,
                        Il2CppGameCore.Version.Minor,
                        Il2CppGameCore.Version.Revision))
                    throw new InvalidDataException(
                        $"Managed module does not support game {Il2CppGameCore.Version.Major}." +
                        $"{Il2CppGameCore.Version.Minor}.{Il2CppGameCore.Version.Revision}.");
                var actualHash = ComputeSha256(assemblyPath);
                if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Assembly SHA-256 does not match the manifest.");
                result.Add(new Candidate
                {
                    AssemblyPath = Path.GetFullPath(assemblyPath),
                    ManifestPath = Path.GetFullPath(manifestPath),
                    Manifest = manifest,
                });
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Managed mod manifest '{Path.GetFileName(manifestPath)}' rejected: {ex.Message}");
            }
        }
        return result;
    }

    private static void Load(Candidate candidate)
    {
        var manifest = candidate.Manifest;
        WriteJsonAtomic(_markerPath, new StartupMarker
        {
            ModId = manifest.Id,
            Sha256 = manifest.Sha256,
            StartedUtc = DateTimeOffset.UtcNow,
        });

        ModContext context = null;
        IAnomalyMod module = null;
        LoadedModule loaded = null;
        try
        {
            var assembly = Assembly.LoadFrom(candidate.AssemblyPath);
            var moduleTypes = assembly.GetTypes()
                .Where(type => typeof(IAnomalyMod).IsAssignableFrom(type) &&
                               !type.IsAbstract && !type.IsInterface)
                .ToArray();
            if (moduleTypes.Length != 1)
                throw new InvalidOperationException(
                    $"Expected exactly one concrete IAnomalyMod type; found {moduleTypes.Length}.");
            if (moduleTypes[0].GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException("The module type must have a public parameterless constructor.");

            module = (IAnomalyMod)Activator.CreateInstance(moduleTypes[0]);
            context = new ModContext(
                manifest.Id,
                _anomalyRoot,
                () => BeginOwnedExecution(manifest),
                () => TryDelete(_markerPath),
                ex => FaultModule(loaded, ex));
            loaded = new LoadedModule
            {
                Candidate = candidate,
                Manifest = manifest,
                Module = module,
                Context = context,
            };
            Loaded.Add(loaded);
            module.Initialize(context);
            module.OnEnable();
            TryDelete(_markerPath);
            SaveState();
            MelonLogger.Msg($"Managed mod loaded: {manifest.Name} {manifest.Version} ({manifest.Id}).");
        }
        catch (Exception ex)
        {
            if (loaded != null) Loaded.Remove(loaded);
            IncrementFailure(manifest.Id);
            LogError($"Managed mod '{manifest.Id}' failed during startup: {ex}");
            if (loaded != null) loaded.Disabled = true;
            Teardown(manifest.Id, module, context);
            // Context disposal clears owner-scoped records; publish the terminal lifecycle
            // failure afterwards so it remains visible to diagnostics.
            FeatureHealth.Report(manifest.Id, "lifecycle", FeatureHealthStatus.Failed, ex.GetType().Name);
            TryDelete(_markerPath);
            SaveState();
        }
    }

    private static void BeginOwnedExecution(ModManifest manifest)
    {
        try
        {
            WriteJsonAtomic(_markerPath, new StartupMarker
            {
                ModId = manifest.Id,
                Sha256 = manifest.Sha256,
                StartedUtc = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not write the execution marker for '{manifest.Id}': {ex.Message}");
        }
    }

    private static void FaultModule(LoadedModule loaded, Exception exception)
    {
        if (loaded == null || loaded.Disabled) return;
        loaded.Disabled = true;
        IncrementFailure(loaded.Manifest.Id);
        LogError($"Managed mod '{loaded.Manifest.Id}' faulted and was disabled: {exception}");
        Teardown(loaded.Manifest.Id, loaded.Module, loaded.Context);
        Loaded.Remove(loaded);
        FeatureHealth.Report(loaded.Manifest.Id, "lifecycle", FeatureHealthStatus.Failed,
            exception.GetType().Name);
        SaveState();
    }

    private static bool Teardown(string modId, IAnomalyMod module, ModContext context)
    {
        bool clean = true;
        IsolatedCleanup.Run((step, exception) =>
        {
            clean = false;
            LogError($"Managed mod '{modId}' {step} failed: {exception}");
        },
            new CleanupStep("disable", () => module?.OnDisable()),
            new CleanupStep("dispose", () => module?.Dispose()),
            new CleanupStep("context cleanup", () => context?.Dispose()));
        return clean && context?.TeardownFailed != true;
    }

    private static void LogError(string message)
    {
        try { MelonLogger.Error(message); }
        catch { /* module teardown must not depend on loader logging */ }
    }

    private static void AttributeInterruptedStartup()
    {
        var marker = ReadJson<StartupMarker>(_markerPath, 64 * 1024);
        if (marker == null || string.IsNullOrWhiteSpace(marker.ModId)) return;
        IncrementFailure(marker.ModId);
        MelonLogger.Warning($"The previous process stopped while loading managed mod '{marker.ModId}'.");
        TryDelete(_markerPath);
        SaveState();
    }

    private static void Quarantine(Candidate candidate, string reason)
    {
        try
        {
            var directory = Path.Combine(_anomalyRoot, "Quarantine", "managed-runtime", reason);
            Directory.CreateDirectory(directory);
            var suffix = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            var assemblyTarget = Path.Combine(directory, suffix + "-" + Path.GetFileName(candidate.AssemblyPath));
            var manifestTarget = assemblyTarget + ".anomaly.json";
            File.Move(candidate.AssemblyPath, assemblyTarget);
            File.Move(candidate.ManifestPath, manifestTarget);
            MelonLogger.Warning($"Managed mod '{candidate.Manifest.Id}' was quarantined after repeated failures.");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Failed to quarantine managed mod '{candidate.Manifest.Id}': {ex.Message}");
        }
    }

    private static int GetFailureCount(string id) =>
        _state.ConsecutiveStartupFailures.TryGetValue(id, out var count) ? count : 0;

    private static void IncrementFailure(string id) =>
        _state.ConsecutiveStartupFailures[id] = Math.Min(GetFailureCount(id) + 1, 1000);

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static T ReadJson<T>(string path, long maxBytes) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > maxBytes) return null;
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    private static void SaveState() => WriteJsonAtomic(_statePath, _state);

    private static void WriteJsonAtomic(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Formatting.Indented));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    private static void TryDelete(string path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
