using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Anomaly.Client.Api.Internal.Diagnostics;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Internal.Modding;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace Anomaly.Client.Diagnostics.News;

/// <summary>Local descriptive telemetry. It never participates in admission or resolves assemblies.</summary>
internal static class NewsDiagnosticService
{
    private const int MaximumIssues = 200;
    private const int MaximumQueuedMessages = 200;
    private sealed class Observation
    {
        internal DiagnosticSeverity Severity;
        internal string Section;
        internal string Message;
        internal DateTimeOffset Timestamp;
    }

    private sealed class BackgroundResult
    {
        internal NewsInventoryResult Inventory;
        internal NewsHistoryResult Logs;
    }

    private static readonly ConcurrentQueue<Observation> Observations = new();
    private static readonly Dictionary<string, DiagnosticIssue> ObservedIssues = new(StringComparer.Ordinal);
    private static NewsDiagnosticSnapshot _snapshot = new() { IsCollecting = true };
    private static NewsDiagnosticSnapshot _fixture;
    private static Task<BackgroundResult> _background;
    private static BackgroundResult _recovered;
    private static DateTimeOffset _processStartedUtc;
    private static DateTimeOffset? _cleanMarkerTimestamp;
    private static string _sessionMarker;
    private static string _fingerprint;
    private static int _initialized;
    private static int _queued;
    private static int _dropped;
    private static int _revision;

    internal static NewsDiagnosticSnapshot Snapshot => _fixture ?? Volatile.Read(ref _snapshot);

    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
        try
        {
            _processStartedUtc = new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime());
            _sessionMarker = "ANOMALY-NEWS-SESSION " + Guid.NewGuid().ToString("N");
            MelonLogger.ErrorCallbackHandler += OnError;
            MelonLogger.WarningCallbackHandler += OnWarning;
            // Both loader outputs contain this marker. Historical filenames alone do not identify a process.
            Core.Instance.LoggerInstance.Msg(_sessionMarker);
        }
        catch (Exception exception)
        {
            ObservedIssues["initialize"] = NewsDiagnosticIdentity.Unavailable("diagnostics", "NEWS-INIT",
                "Startup evidence capture could not initialize: " + exception.GetType().Name, false);
        }
    }

    internal static void ObserveCleanShutdownMarker(DateTime timestampUtc) =>
        _cleanMarkerTimestamp = new DateTimeOffset(DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc));

    internal static void Shutdown()
    {
        if (Interlocked.Exchange(ref _initialized, 0) == 0) return;
        MelonLogger.ErrorCallbackHandler -= OnError;
        MelonLogger.WarningCallbackHandler -= OnWarning;
    }

    private static void OnError(string section, string message) => Enqueue(DiagnosticSeverity.Error, section, message);
    private static void OnWarning(string section, string message) => Enqueue(DiagnosticSeverity.Warning, section, message);

    private static void Enqueue(DiagnosticSeverity severity, string section, string message)
    {
        // Callbacks can run on loader/transport threads. No Unity, file I/O, logging or regex work here.
        if (string.IsNullOrEmpty(message) || message.StartsWith("News diagnostics:", StringComparison.Ordinal) ||
            severity != DiagnosticSeverity.Error && (message.StartsWith("Diagnostics:", StringComparison.Ordinal) ||
            message.StartsWith("Diagnostic artifact written locally:", StringComparison.Ordinal))) return;
        if (Interlocked.Increment(ref _queued) > MaximumQueuedMessages)
        {
            Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _dropped);
            return;
        }
        Observations.Enqueue(new Observation
        {
            Severity = severity,
            Section = Literal(section, 256),
            Message = message.Length <= 8192 ? message : message.Substring(0, 8192),
            Timestamp = DateTimeOffset.UtcNow,
        });
    }

    internal static void Refresh()
    {
        if (_fixture != null) return;
        try
        {
            var issues = new List<DiagnosticIssue>();
            var components = CaptureRegistry(issues).Concat(ManagedModRuntime.GetDiagnosticComponents()).ToList();
            if (_background == null)
            {
                string mods = MelonEnvironment.ModsDirectory, plugins = MelonEnvironment.PluginsDirectory;
                string libs = MelonEnvironment.UserLibsDirectory, logs = MelonEnvironment.MelonLoaderLogsDirectory;
                string latest = Path.Combine(MelonEnvironment.MelonLoaderDirectory, "Latest.log");
                string reports = Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly", "CrashReports");
                bool noFolders = LoaderConfig.Current.Loader.DisableSubFolderLoad;
                bool noManifest = LoaderConfig.Current.Loader.DisableSubFolderManifest;
                var detached = components.ToArray();
                _background = Task.Run(() =>
                {
                    var inventory = NewsMetadataInventory.Scan(mods, plugins, libs, noFolders, noManifest, detached, Redact);
                    var merged = new Dictionary<string, DiagnosticComponent>(StringComparer.Ordinal);
                    foreach (var component in detached) merged[component.Key] = component;
                    foreach (var component in inventory.Components) merged[component.Key] = component;
                    var all = merged.Values.ToArray();
                    return new BackgroundResult
                    {
                        Inventory = inventory,
                        Logs = NewsHistoryReader.Read(latest, logs, reports, _processStartedUtc, _sessionMarker, all, Redact),
                    };
                });
            }
            if (_background.IsCompleted && _recovered == null)
            {
                if (_background.IsCompletedSuccessfully) _recovered = _background.Result;
                else _recovered = new BackgroundResult
                {
                    Inventory = new NewsInventoryResult(),
                    Logs = new NewsHistoryResult
                    {
                        CurrentIssues = new[] { NewsDiagnosticIdentity.Unavailable("diagnostics", "NEWS-READ",
                            "Startup files could not be inspected.", false) },
                    },
                };
            }
            if (_recovered != null)
            {
                // Live registrations replace descriptive disk-only rows, including dynamically registered mods.
                var livePaths = new HashSet<string>(components.Select(component => NewsDiagnosticIdentity.NormalizePath(component.Path)), StringComparer.OrdinalIgnoreCase);
                foreach (var disk in _recovered.Inventory.Components)
                {
                    int index = components.FindIndex(component => component.Key == disk.Key);
                    if (index >= 0 && string.IsNullOrEmpty(components[index].Version))
                    {
                        var observed = components[index];
                        components[index] = new DiagnosticComponent
                        {
                            Key = observed.Key, Path = observed.Path, TypeName = observed.TypeName, Kind = observed.Kind, State = observed.State,
                            Name = disk.Name, Version = disk.Version, Id = string.IsNullOrEmpty(observed.Id) ? disk.Id : observed.Id,
                        };
                    }
                    else if (index < 0 && !livePaths.Contains(NewsDiagnosticIdentity.NormalizePath(disk.Path))) components.Add(disk);
                }
                issues.AddRange(_recovered.Inventory.Issues);
                issues.AddRange(_recovered.Logs.CurrentIssues);
            }
            var unique = components.GroupBy(component => component.Key).Select(group => NormalizeComponent(group.First())).ToArray();
            while (Observations.TryDequeue(out var observation))
            {
                Interlocked.Decrement(ref _queued);
                foreach (var issue in NewsLogParser.ParseMessage(observation.Severity, observation.Section,
                    observation.Message, unique, "loader-callback", observation.Timestamp, false, Redact))
                    if (ObservedIssues.Count < MaximumIssues || ObservedIssues.ContainsKey(issue.Key)) ObservedIssues[issue.Key] = issue;
            }
            issues.AddRange(ObservedIssues.Values);
            var moduleIssues = ManagedModRuntime.GetDiagnosticIssues();
            issues.AddRange(moduleIssues.Where(issue => !issue.IsHistorical));
            foreach (var feature in FeatureHealth.Snapshot().Values.Where(feature => feature.Status != FeatureHealthStatus.Healthy))
            {
                var owner = NewsDiagnosticIdentity.Match(unique, id: feature.OwnerId);
                if (feature.OwnerId == "anomaly") owner = unique.FirstOrDefault(component => component.Id == "anomaly.client");
                string summary = Literal(feature.FeatureId, 128) + ": " + feature.Status;
                if (!string.IsNullOrWhiteSpace(feature.Detail)) summary += " — " + Redact(feature.Detail);
                if (!string.IsNullOrWhiteSpace(feature.Remediation)) summary += "\n" + Redact(feature.Remediation);
                issues.Add(new DiagnosticIssue
                {
                    Key = "health:" + feature.OwnerId + ":" + feature.FeatureId,
                    ComponentKey = owner?.Key ?? string.Empty,
                    ComponentId = feature.OwnerId,
                    ComponentName = owner?.Name ?? feature.OwnerId,
                    Kind = DiagnosticIssueKind.FeatureHealth,
                    Severity = feature.Status == FeatureHealthStatus.Failed ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    Summary = summary,
                    Timestamp = feature.UpdatedUtc,
                    Source = "FeatureHealth",
                });
            }
            bool limited = _dropped > 0 || issues.Count > MaximumIssues || ObservedIssues.Count >= MaximumIssues;
            var current = Deduplicate(issues.Select(issue => Rematch(issue, unique)))
                .OrderByDescending(issue => issue.Severity).Take(MaximumIssues - (limited ? 1 : 0)).ToArray();
            if (limited)
                current = new[] { NewsDiagnosticIdentity.Unavailable("diagnostics", "NEWS-LIMIT",
                    "The bounded issue limit was reached; consult the local log for additional messages.", false) }.Concat(current).ToArray();
            var rejected = new HashSet<string>(current.Where(issue => issue.Kind == DiagnosticIssueKind.LoadFailure ||
                issue.Kind == DiagnosticIssueKind.InitializationError).Select(issue => issue.ComponentKey), StringComparer.Ordinal);
            unique = unique.Select(component => NormalizeComponent(component,
                rejected.Contains(component.Key) && component.State != DiagnosticLoadState.Disabled
                    ? DiagnosticLoadState.Failed : component.State)).ToArray();
            var history = new List<DiagnosticIssue>(_recovered?.Logs.History ?? Array.Empty<DiagnosticIssue>());
            history.AddRange(moduleIssues.Where(issue => issue.IsHistorical));
            if (_cleanMarkerTimestamp.HasValue)
                history.Add(new DiagnosticIssue
                {
                    Key = "clean-marker", Kind = DiagnosticIssueKind.DiagnosticReport, Severity = DiagnosticSeverity.Info,
                    Source = "last-clean-shutdown.marker", Code = "CLEAN-SHUTDOWN-MARKER",
                    Summary = "A clean shutdown was recorded at this time. It may be older than the immediately preceding launch.",
                    Timestamp = _cleanMarkerTimestamp, IsHistorical = true,
                });
            else
                history.Add(new DiagnosticIssue
                {
                    Key = "no-clean-marker", Kind = DiagnosticIssueKind.DiagnosticReport, Severity = DiagnosticSeverity.Info,
                    Source = "last-clean-shutdown.marker", Code = "NO-CLEAN-SHUTDOWN-MARKER",
                    Summary = "No clean shutdown marker was found. This does not establish a crash.", IsHistorical = true,
                });
            var runtime = CaptureRuntime();
            string game = $"{Il2CppGameCore.Version.Major}.{Il2CppGameCore.Version.Minor}.{Il2CppGameCore.Version.Revision}";
            string core = AssemblyVersion(typeof(Core).Assembly);
            string loader = MelonLoader.Properties.BuildInfo.Version ?? AssemblyVersion(typeof(MelonBase).Assembly);
            bool collecting = !_background.IsCompleted;
            string fingerprint = NewsDiagnosticIdentity.Hash(game + core + loader + collecting + Tr.ActiveLocale +
                string.Join("|", unique.Select(component => component.Key + component.Name + component.Version + component.State)) +
                string.Join("|", current.Select(issue => issue.Key + issue.Summary + issue.ComponentKey)) +
                string.Join("|", history.Select(issue => issue.Key + issue.Summary)) + string.Join("|", runtime.Select(value => value.Key + value.Value)));
            if (fingerprint == _fingerprint) return;
            _fingerprint = fingerprint;
            Volatile.Write(ref _snapshot, new NewsDiagnosticSnapshot
            {
                Revision = ++_revision, IsCollecting = collecting, GameVersion = game, AnomalyVersion = core, LoaderVersion = loader,
                Components = unique.OrderBy(component => component.Kind).ThenBy(component => component.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
                CurrentIssues = current.OrderByDescending(issue => issue.Severity).ToArray(),
                History = Deduplicate(history).OrderByDescending(issue => issue.Timestamp).Take(MaximumIssues).ToArray(),
                Runtime = runtime,
            });
        }
        catch (Exception exception)
        {
            // A transient read/registry failure must not pin this fallback after data recovers.
            _fingerprint = null;
            var previous = _snapshot;
            Volatile.Write(ref _snapshot, new NewsDiagnosticSnapshot
            {
                Revision = ++_revision,
                IsCollecting = previous.IsCollecting,
                GameVersion = previous.GameVersion, AnomalyVersion = previous.AnomalyVersion, LoaderVersion = previous.LoaderVersion,
                Components = previous.Components, Runtime = previous.Runtime, History = previous.History,
                CurrentIssues = new[] { NewsDiagnosticIdentity.Unavailable("diagnostics", "NEWS-SNAPSHOT",
                    "Diagnostic snapshot could not be refreshed: " + exception.GetType().Name, false) },
            });
        }
    }

    private static DiagnosticComponent[] CaptureRegistry(List<DiagnosticIssue> issues)
    {
        var result = new List<DiagnosticComponent>();
        var melons = new HashSet<MelonBase>(MelonBase.RegisteredMelons);
        foreach (var assembly in MelonAssembly.LoadedAssemblies)
        {
            foreach (var melon in assembly.LoadedMelons) melons.Add(melon);
            foreach (var rotten in assembly.RottenMelons)
            {
                string path = assembly.Location ?? rotten.assembly?.Location ?? string.Empty;
                string typeName = rotten.type?.FullName ?? string.Empty;
                var kind = rotten.type != null && typeof(MelonPlugin).IsAssignableFrom(rotten.type)
                    ? DiagnosticComponentKind.Plugin : IsInside(path, MelonEnvironment.PluginsDirectory)
                        ? DiagnosticComponentKind.Plugin : DiagnosticComponentKind.Mod;
                string key = NewsDiagnosticIdentity.ComponentKey(kind, path, typeName: typeName);
                string name = rotten.type?.Name ?? Path.GetFileNameWithoutExtension(path);
                result.Add(new DiagnosticComponent { Key = key, Name = Literal(name), Path = path, TypeName = typeName, Kind = kind, State = DiagnosticLoadState.Failed });
                string summary = Redact(rotten.errorMessage + "\n" + rotten.exception);
                issues.Add(new DiagnosticIssue
                {
                    Key = NewsDiagnosticIdentity.IssueKey(DiagnosticIssueKind.LoadFailure, Path.GetFileName(path), key, "ROTTEN-MELON", summary),
                    ComponentKey = key, ComponentName = name, ComponentPath = path, Kind = DiagnosticIssueKind.LoadFailure,
                    Severity = DiagnosticSeverity.Error, Code = "ROTTEN-MELON", Source = Path.GetFileName(path), Summary = summary,
                });
            }
        }
        foreach (var melon in melons)
        {
            string path = melon.MelonAssembly?.Location ?? melon.GetType().Assembly.Location;
            string typeName = melon.GetType().FullName ?? string.Empty;
            var kind = melon is MelonPlugin ? DiagnosticComponentKind.Plugin : DiagnosticComponentKind.Mod;
            result.Add(new DiagnosticComponent
            {
                Key = NewsDiagnosticIdentity.ComponentKey(kind, path, melon.ID, typeName), Path = path, TypeName = typeName,
                Name = Literal(melon.Info?.Name ?? melon.GetType().Name), Version = Literal(melon.Info?.Version), Id = Literal(melon.ID),
                Kind = kind, State = melon.Registered ? DiagnosticLoadState.Registered : DiagnosticLoadState.Present,
            });
        }
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            string path;
            try { path = assembly.Location; } catch { continue; }
            if (!IsInside(path, MelonEnvironment.UserLibsDirectory)) continue;
            result.Add(new DiagnosticComponent
            {
                Key = NewsDiagnosticIdentity.ComponentKey(DiagnosticComponentKind.Dependency, path), Path = path,
                Name = Literal(assembly.GetName().Name), Version = AssemblyVersion(assembly),
                Kind = DiagnosticComponentKind.Dependency, State = DiagnosticLoadState.Loaded,
            });
        }
        return result.ToArray();
    }

    private static DiagnosticValue[] CaptureRuntime()
    {
        string LoadedVersion(string name) => AssemblyVersion(AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal)));
        return new[]
        {
            new DiagnosticValue { Key = "api", Value = AssemblyVersion(typeof(FeatureHealth).Assembly) },
            new DiagnosticValue { Key = "bootstrap", Value = LoadedVersion("Anomaly.Bootstrap") },
            new DiagnosticValue { Key = "unity", Value = Application.unityVersion },
            new DiagnosticValue { Key = "dotnet", Value = Environment.Version.ToString() },
            new DiagnosticValue { Key = "harmony", Value = LoadedVersion("0Harmony") },
            new DiagnosticValue { Key = "interop", Value = LoadedVersion("Il2CppInterop.Runtime") },
            new DiagnosticValue { Key = "os", Value = Environment.OSVersion.ToString() },
            new DiagnosticValue { Key = "gpu", Value = SystemInfo.graphicsDeviceName },
            new DiagnosticValue { Key = "graphics", Value = SystemInfo.graphicsDeviceType.ToString() },
            new DiagnosticValue { Key = "resolution", Value = Screen.width + " × " + Screen.height },
        };
    }

    private static DiagnosticIssue Rematch(DiagnosticIssue issue, DiagnosticComponent[] components)
    {
        var component = components.FirstOrDefault(component => component.Key == issue.ComponentKey) ??
            NewsDiagnosticIdentity.Match(components, issue.ComponentPath, issue.ComponentName, issue.ComponentId);
        return new DiagnosticIssue
        {
            Key = issue.Key, ComponentKey = component?.Key ?? string.Empty, ComponentPath = issue.ComponentPath,
            ComponentName = component?.Name ?? Literal(issue.ComponentName), ComponentId = issue.ComponentId,
            Kind = issue.Kind, Severity = issue.Severity, Source = Literal(issue.Source), Code = Literal(issue.Code),
            Summary = Redact(issue.Summary), Timestamp = issue.Timestamp, IsHistorical = issue.IsHistorical,
            DependencyName = Literal(issue.DependencyName), DependencyVersion = Literal(issue.DependencyVersion),
        };
    }

    private static DiagnosticComponent NormalizeComponent(DiagnosticComponent component, DiagnosticLoadState? state = null) => new()
    {
        Key = component.Key, Path = component.Path, Name = Literal(component.Name), Version = Literal(component.Version),
        Id = Literal(component.Id), TypeName = Literal(component.TypeName, 512), Kind = component.Kind, State = state ?? component.State,
    };

    private static IEnumerable<DiagnosticIssue> Deduplicate(IEnumerable<DiagnosticIssue> issues) => issues
        .GroupBy(issue => issue.Kind + "|" + issue.ComponentKey + "|" + issue.Source + "|" + issue.Summary)
        .Select(group => group.First());

    private static bool IsInside(string path, string root) => !string.IsNullOrWhiteSpace(path) &&
        NewsDiagnosticIdentity.NormalizePath(path).StartsWith(NewsDiagnosticIdentity.NormalizePath(root) + "/", StringComparison.OrdinalIgnoreCase);

    private static string AssemblyVersion(Assembly assembly) => assembly?.GetName().Version?.ToString() ?? string.Empty;
    private static string Redact(string text) => DiagnosticTextRedactor.Redact(text, 1200) ?? string.Empty;
    private static string Literal(string text, int limit = 256) => NewsDiagnosticIdentity.SafeText(text, null, limit).Replace('\n', ' ').Replace('\t', ' ');

    internal static string IssueSummary(DiagnosticIssue issue) =>
        !string.IsNullOrEmpty(issue.DependencyName)
            ? Tr.Get(TrKeys.NewsDiagnostics_Missing_Dependency_Detail, Literal(issue.DependencyName), Literal(issue.DependencyVersion))
            : issue.Kind == DiagnosticIssueKind.InterruptedActivity
                ? Tr.Get(TrKeys.NewsDiagnostics_Interrupted_Activity, Literal(issue.ComponentName))
                : Redact(issue.Summary);

    internal static string CopySummary()
    {
        var snapshot = Snapshot;
        var text = new StringBuilder(Tr.Get(TrKeys.NewsDiagnostics_Title)).AppendLine();
        text.AppendLine(Tr.Get(TrKeys.NewsDiagnostics_Versions, snapshot.GameVersion, snapshot.AnomalyVersion, snapshot.LoaderVersion));
        if (snapshot.IsCollecting) text.AppendLine(Tr.Get(TrKeys.NewsDiagnostics_Collecting));
        text.AppendLine(Tr.Get(TrKeys.NewsDiagnostics_Runtime));
        foreach (var value in snapshot.Runtime)
            text.AppendLine(Tr.Get("anomaly.news.diagnostics.runtime." + value.Key) + ": " + Literal(value.Value, 512));
        foreach (var component in snapshot.Components.Take(512))
            text.AppendLine(Literal(component.Name) + " " + Literal(component.Version) + " — " +
                Tr.Get("anomaly.news.diagnostics.state." + component.State.ToString().ToLowerInvariant()));
        text.AppendLine(Tr.Get(TrKeys.NewsDiagnostics_Issues, snapshot.CurrentIssues.Length));
        foreach (var issue in snapshot.CurrentIssues.Take(MaximumIssues))
            text.AppendLine(Literal(issue.ComponentName) + " " + Literal(issue.Code) + ": " + IssueSummary(issue));
        text.AppendLine(Tr.Get(TrKeys.NewsDiagnostics_History, snapshot.History.Length));
        foreach (var issue in snapshot.History.Take(MaximumIssues))
            text.AppendLine(issue.Timestamp?.ToString("u") + " " + Literal(issue.Source) + ": " + IssueSummary(issue));
        text.AppendLine("<game>/MelonLoader/Latest.log");
        text.AppendLine("<MelonLoader UserData>/Anomaly/CrashReports/");
        text.Append(Tr.Get(TrKeys.NewsDiagnostics_History_Limit));
        return text.ToString();
    }

    // Internal fixture seam for the isolated engine smoke harness; never a mod-facing API.
    internal static void SetFixture(NewsDiagnosticSnapshot snapshot)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANOMALY_UI_SMOKE_DIR"))) _fixture = snapshot;
    }
    internal static void ClearFixture() => _fixture = null;
}
