using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using Anomaly.Client.Diagnostics.News;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class NewsDiagnosticsTests
{
    private const string Marker = "ANOMALY-NEWS-SESSION 0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Multiline_loader_failure_matches_exact_path_and_ignores_debug_misses()
    {
        using var files = new Fixture();
        string path = Path.Combine(files.Root, "Example.dll");
        var component = Component(path);
        string log = "[10:00:00.001] [DEBUG] [MelonAssemblyResolver] Failed to find Optional in any search directories\n" +
            $"[10:00:00.002] [ERROR] Failed to load Melon Assembly from '{path}':\n" +
            "System.FileNotFoundException: Missing dep\n   at loader.call()\n" +
            "[10:00:00.003] Done\n";

        var issues = NewsLogParser.Parse(log, new[] { component }, "Latest.log", redact: text => text.Replace("Missing dep", "[safe]"));

        var issue = Assert.Single(issues);
        Assert.Equal(component.Key, issue.ComponentKey);
        Assert.Equal(DiagnosticIssueKind.LoadFailure, issue.Kind);
        Assert.Contains("[safe]", issue.Summary);
        Assert.Contains("loader.call", issue.Summary);
        Assert.DoesNotContain("Optional", issue.Summary);
    }

    [Fact]
    public void Missing_dependency_warning_preserves_registered_state_and_names_each_dependency()
    {
        var component = Component("Example.dll", state: DiagnosticLoadState.Registered);
        string message = "Some Melons are missing dependencies, which you may have to install.\n" +
            "If these are optional dependencies, mark them as optional.\n" +
            "- 'Example Mod' is missing the following dependencies:\n" +
            "    - 'Required.Library' v1.2.3.0\n    - 'Another.Library' v4.0.0.0";

        var issues = NewsLogParser.ParseMessage(DiagnosticSeverity.Warning, "", message, new[] { component }, "Latest.log",
            redact: text => text.Replace("Required.Library", "[endpoint-redacted]"));

        Assert.Equal(2, issues.Length);
        Assert.All(issues, issue =>
        {
            Assert.Equal(DiagnosticIssueKind.MissingDependency, issue.Kind);
            Assert.Equal(DiagnosticSeverity.Warning, issue.Severity);
            Assert.Equal(component.Key, issue.ComponentKey);
        });
        Assert.Equal(DiagnosticLoadState.Registered, component.State);
        Assert.Equal("Required.Library", issues[0].DependencyName);
        Assert.Equal("1.2.3.0", issues[0].DependencyVersion);
        Assert.Contains("[endpoint-redacted]", issues[0].Summary);
    }

    [Fact]
    public void Registered_mod_with_logged_error_stays_registered_and_ambiguous_names_stay_global()
    {
        var first = Component("first.dll", state: DiagnosticLoadState.Registered);
        var second = Component("second.dll", state: DiagnosticLoadState.Registered);
        var matched = Assert.Single(NewsLogParser.ParseMessage(DiagnosticSeverity.Error,
            "example.mod:Example_Mod", "System.Exception: init error", new[] { first }, "live"));
        Assert.Equal(first.Key, matched.ComponentKey);
        Assert.Equal(DiagnosticIssueKind.ReportedError, matched.Kind);
        Assert.Equal(DiagnosticLoadState.Registered, first.State);

        var ambiguous = Assert.Single(NewsLogParser.ParseMessage(DiagnosticSeverity.Error,
            "Example Mod", "System.Exception: init error", new[] { first, second }, "live"));
        Assert.Empty(ambiguous.ComponentKey);
        Assert.Empty(ambiguous.ComponentPath);
        var substring = Assert.Single(NewsLogParser.ParseMessage(DiagnosticSeverity.Error,
            "Example Mod Extra", "error", new[] { first }, "live"));
        Assert.Empty(substring.ComponentKey);
    }

    [Fact]
    public void Explicit_missing_path_does_not_bind_a_different_registered_file_with_the_same_name()
    {
        using var files = new Fixture();
        var registered = Component(files.Path("Mods/registered.dll"), DiagnosticLoadState.Registered);
        string missingPath = files.Path("Mods/old-copy.dll");
        var issue = Assert.Single(NewsLogParser.ParseMessage(DiagnosticSeverity.Error, "Example Mod",
            $"Failed to load Melon 'Example Mod' from '{missingPath}': loader rejected it.",
            new[] { registered }, "log"));

        Assert.Equal(DiagnosticIssueKind.LoadFailure, issue.Kind);
        Assert.Empty(issue.ComponentKey);
        Assert.Equal(NewsDiagnosticIdentity.NormalizePath(missingPath), issue.ComponentPath);
        Assert.Null(NewsDiagnosticIdentity.Match(new[] { registered }, path: issue.ComponentPath,
            name: issue.ComponentName, id: registered.Id, section: "Example Mod"));
        Assert.Equal(DiagnosticLoadState.Registered, registered.State);
    }

    [Fact]
    public void Incompatibility_and_dependency_cycles_are_explicit_failures()
    {
        var component = Component("example.dll");
        var cycle = Assert.Single(NewsLogParser.ParseMessage(DiagnosticSeverity.Error, "",
            "Some Melons could not be loaded due to a cyclic dependency:\n- 'Example Mod'", new[] { component }, "log"));
        Assert.Equal("dependency-cycle", cycle.Code);
        Assert.Equal(component.Key, cycle.ComponentKey);
        var rejection = Assert.Single(NewsLogParser.ParseMessage(DiagnosticSeverity.Info, "",
            "'Example Mod v1.0.0' is incompatible:", new[] { component }, "log"));
        Assert.Equal(DiagnosticIssueKind.LoadFailure, rejection.Kind);
    }

    [Fact]
    public void Loader_initialization_frame_identifies_phase_without_changing_registered_state()
    {
        var component = Component("example.dll", DiagnosticLoadState.Registered);
        string message = "System.InvalidOperationException: initialize failed\n" +
            "   at Example.Core.OnInitializeMelon()\n" +
            "   at MelonLoader.MelonBase.LoaderInitialized() in loader.cs:line 580";
        var known = Assert.Single(NewsLogParser.ParseMessage(DiagnosticSeverity.Error, "Example Mod", message,
            new[] { component }, "log"));
        Assert.Equal(DiagnosticIssueKind.InitializationError, known.Kind);
        Assert.Equal(component.Key, known.ComponentKey);
        Assert.Equal(DiagnosticLoadState.Registered, component.State);
        var unknown = Assert.Single(NewsLogParser.ParseMessage(DiagnosticSeverity.Error, "unknown", message,
            new[] { component }, "log"));
        Assert.Equal(DiagnosticIssueKind.ReportedError, unknown.Kind);
        Assert.Empty(unknown.ComponentKey);
    }

    [Theory]
    [InlineData("Diagnostics: Watchdog and crash handlers initialized. Minidumps are disabled.")]
    [InlineData("Diagnostic artifact written locally: crash.json. Review before sharing.")]
    public void Informational_diagnostic_policy_warnings_are_not_component_issues(string message)
    {
        Assert.Empty(NewsLogParser.ParseMessage(DiagnosticSeverity.Warning, "Anomaly", message,
            Array.Empty<DiagnosticComponent>(), "log"));
    }

    [Fact]
    public void Exact_session_archive_prevents_duplicate_latest_import_and_keeps_previous_launch_separate()
    {
        using var files = new Fixture();
        DateTimeOffset started = Now.AddMinutes(-10);
        string currentLog = "[19:50:00.001] [ERROR] Early plugin error\n[19:50:01.001] [Anomaly] " + Marker + "\n";
        files.Write("Latest.log", currentLog, Now);
        files.Write("Logs/current.log", currentLog, Now);
        files.Write("Logs/previous.log", "[18:00:00.001] [ERROR] Previous plugin error\n", started.AddHours(-1));
        // A copied current log with stale file metadata must not masquerade as a prior launch.
        files.Write("Logs/stale-copy.log", currentLog, started.AddHours(-2));

        var result = files.Read(started);

        var current = Assert.Single(result.CurrentIssues);
        Assert.Equal("current.log", current.Source);
        Assert.Contains("Early plugin error", current.Summary);
        var history = Assert.Single(result.History);
        Assert.True(history.IsHistorical);
        Assert.Equal("previous.log", history.Source);
    }

    [Fact]
    public void Missing_or_mixed_session_markers_cannot_import_an_unverified_current_log()
    {
        using var files = new Fixture();
        files.Write("Latest.log", "[19:50:00.001] [ERROR] Error from unknown launch\n", Now);
        var missing = files.Read(Now.AddMinutes(-10));
        Assert.All(missing.CurrentIssues, issue => Assert.Equal(DiagnosticIssueKind.EvidenceUnavailable, issue.Kind));

        files.Write("Latest.log", "[19:50:00.001] [Anomaly] " + Marker + "\n" +
            "[19:50:01.001] [Anomaly] ANOMALY-NEWS-SESSION aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n" +
            "[19:50:02.001] [ERROR] Mixed-launch error\n", Now);
        var mixed = files.Read(Now.AddMinutes(-10));
        Assert.All(mixed.CurrentIssues, issue => Assert.Equal(DiagnosticIssueKind.EvidenceUnavailable, issue.Kind));
    }

    [Fact]
    public void Quoted_marker_substrings_are_not_current_session_markers_and_post_marker_errors_are_not_backfilled()
    {
        using var files = new Fixture();
        DateTimeOffset started = Now.AddMinutes(-10);
        files.Write("Latest.log", "[19:50:00.001] [ERROR] copied text '" + Marker + "'\n", Now);
        Assert.Contains(files.Read(started).CurrentIssues, issue => issue.Code == "current-log-unverified");
        files.Write("Latest.log", "[19:50:00.001] [ERROR] startup issue\n[19:50:01.001] [Anomaly] " + Marker +
            "\n[19:50:02.001] [ERROR] live callback issue\n", Now);
        var backfill = Assert.Single(files.Read(started).CurrentIssues);
        Assert.Equal("startup issue", backfill.Summary);
    }

    [Fact]
    public void Concurrent_writer_handles_and_partial_final_lines_are_safe_to_read()
    {
        using var files = new Fixture();
        string path = files.Path("Latest.log");
        using var writer = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        byte[] bytes = Encoding.UTF8.GetBytes("[19:50:00.001] [ERROR] partial error\n[19:50:00.002] [Anomaly] " + Marker);
        writer.Write(bytes);
        writer.Flush();

        var result = files.Read(Now.AddMinutes(-10));

        Assert.Contains(result.CurrentIssues, issue => issue.Kind == DiagnosticIssueKind.ReportedError && issue.Summary == "partial error");
        Assert.DoesNotContain(result.CurrentIssues, issue => issue.Code == "current-log-unverified");
    }

    [Fact]
    public void History_limits_previous_logs_to_three_within_seven_days()
    {
        using var files = new Fixture();
        DateTimeOffset started = Now.AddMinutes(-10);
        files.Write("Latest.log", "[19:50:00.001] [Anomaly] " + Marker + "\n", Now);
        for (int i = 1; i <= 6; i++)
            files.Write("Logs/prior-" + i + ".log", $"[1{i}:00:00.001] [ERROR] previous {i}\n", started.AddHours(-i));
        files.Write("Logs/too-old.log", "[01:00:00.001] [ERROR] stale\n", Now.AddDays(-8));

        var result = files.Read(started);

        Assert.Equal(3, result.History.Length);
        Assert.DoesNotContain(result.History, issue => issue.Source == "too-old.log");
    }

    [Fact]
    public void Recent_nonfatal_reports_and_native_text_keep_report_kind_without_component_attribution()
    {
        using var files = new Fixture();
        DateTimeOffset started = Now.AddMinutes(-10);
        files.Write("Latest.log", "[19:50:00.001] [Anomaly] " + Marker + "\n", Now);
        files.WriteReport("Reports/crash-flood.json", "FirstChanceFlood", started.AddHours(-1),
            "secret=private-value", "System.Exception");
        files.Write("Reports/crash-native.txt", "ArtifactId: abc\nNative crash at " + started.AddHours(-2).ToString("O") +
            "\nMiniDump: crash-native.dmp\n", started.AddHours(-2));
        var result = files.Read(started, text => text.Replace("secret=private-value", "secret=[redacted]"));

        Assert.Equal(2, result.History.Length);
        var flood = Assert.Single(result.History.Where(issue => issue.Code == "FirstChanceFlood"));
        Assert.DoesNotContain("crash", flood.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted]", flood.Summary);
        Assert.Empty(flood.ComponentKey);
        var native = Assert.Single(result.History.Where(issue => issue.Code == "NativeCrash"));
        Assert.Contains("attribution is unavailable", native.Summary);
        Assert.Empty(native.ComponentName);
    }

    [Fact]
    public void Report_timestamp_classifies_current_evidence_and_corrupt_reports_are_explicit()
    {
        using var files = new Fixture();
        DateTimeOffset started = Now.AddMinutes(-10);
        files.Write("Latest.log", "[19:50:00.001] [Anomaly] " + Marker + "\n", Now);
        files.WriteReport("Reports/crash-current.json", "UnityException", Now.AddMinutes(-1), "caught exception");
        File.SetLastWriteTimeUtc(files.Path("Reports/crash-current.json"), started.AddHours(-1).UtcDateTime);
        files.Write("Reports/crash-invalid.json", "{broken", started.AddHours(-1));

        var result = files.Read(started);

        var issue = Assert.Single(result.CurrentIssues);
        Assert.Equal("UnityException", issue.Code);
        Assert.False(issue.IsHistorical);
        Assert.Contains(result.History, item => item.Code == "report-invalid");
    }

    [Fact]
    public void Reports_use_thirty_day_retention_and_twenty_file_limit()
    {
        using var files = new Fixture();
        DateTimeOffset started = Now.AddMinutes(-10);
        files.Write("Latest.log", "[19:50:00.001] [Anomaly] " + Marker + "\n", Now);
        files.WriteReport("Reports/crash-retained.json", "HangDetected", Now.AddDays(-20), "recovered hang");
        Assert.Contains(files.Read(started).History, issue => issue.Code == "HangDetected");
        files.WriteReport("Reports/crash-expired.json", "HangDetected", Now.AddDays(-31), "expired hang");
        for (int i = 1; i <= 21; i++)
            files.WriteReport("Reports/crash-recent-" + i + ".json", "TaskException", Now.AddHours(-i), "task error");

        var result = files.Read(started);

        Assert.Equal(20, result.History.Count(issue => issue.Kind == DiagnosticIssueKind.DiagnosticReport));
        Assert.Contains(result.History, issue => issue.Code == "report-limit");
        Assert.DoesNotContain(result.History, issue => issue.Source == "crash-expired.json");
    }

    [Fact]
    public void Current_log_and_report_read_budgets_report_truncation_explicitly()
    {
        using var files = new Fixture();
        DateTimeOffset started = Now.AddMinutes(-10);
        files.Write("Latest.log", "[19:50:00.001] [Anomaly] " + Marker + "\n" + new string('x', 4 * 1024 * 1024), Now);
        files.Write("Reports/crash-large.json", new string('x', 1024 * 1024 + 1), started.AddHours(-1));

        var result = files.Read(started);

        Assert.Contains(result.CurrentIssues, issue => issue.Code == "evidence-truncated");
        Assert.Contains(result.History, issue => issue.Code == "evidence-truncated");
        Assert.DoesNotContain(result.History, issue => issue.Code == "report-invalid");
    }

    [Fact]
    public void Clean_marker_snapshot_survives_deletion_and_missing_marker_does_not_claim_crash()
    {
        using var files = new Fixture();
        string path = files.Path("last-clean-shutdown.marker");
        var missing = NewsHistoryReader.ReadCleanShutdownMarker(path);
        Assert.Contains("unknown", missing.Summary);
        Assert.DoesNotContain("crash", missing.Summary, StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(path, Now.AddHours(-1).ToString("O"));
        var snapshot = NewsHistoryReader.ReadCleanShutdownMarker(path);
        File.Delete(path);
        Assert.Equal("clean-shutdown", snapshot.Code);
        Assert.Equal(Now.AddHours(-1), snapshot.Timestamp);
    }

    [Fact]
    public void Metadata_discovery_is_nonexecuting_and_respects_subfolder_gates_and_exclusions()
    {
        using var files = new Fixture();
        files.WriteAssembly("Mods/root.dll", "Fixture.Root", "Root Mod", "1.2.3", "root.mod");
        files.WriteAssembly("Mods/Bundle/child.dll", "Fixture.Child", "Child Mod", "2.0.0", "child.mod");
        files.Write("Mods/Bundle/manifest.json", "deliberately not parsed", Now);
        files.WriteAssembly("Mods/Bundle/Deep/grandchild.dll", "Fixture.Deep", "Deep Mod", "3.0.0", "deep.mod");
        files.WriteAssembly("Mods/Ungated/hidden.dll", "Fixture.Ungated", "Ungated Mod", "1.0.0", "ungated.mod");
        files.WriteAssembly("Mods/Disabled/disabled.dll", "Fixture.Disabled", "Disabled Mod", "1.0.0", "disabled.mod");
        files.Write("Mods/Disabled/manifest.json", "", Now);

        var result = files.Scan();

        Assert.Equal(3, result.Components.Length);
        Assert.All(result.Components, component => Assert.Equal(DiagnosticLoadState.Present, component.State));
        Assert.Contains(result.Components, component => component.Name == "Root Mod" && component.Version == "1.2.3" && component.Id == "root.mod");
        Assert.Empty(result.Issues);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(),
            assembly => assembly.GetName().Name?.StartsWith("Fixture.", StringComparison.Ordinal) == true);
        Assert.Single(files.Scan(disableSubfolders: true).Components);
        Assert.Equal(4, files.Scan(disableManifest: true).Components.Length);
    }

    [Fact]
    public void Duplicate_assembly_copies_remain_present_and_registered_paths_are_not_duplicated()
    {
        using var files = new Fixture();
        files.WriteAssembly("Mods/first.dll", "Same.Assembly", "Example Mod", "1.0.0", "example.mod", new Version(1, 0));
        files.WriteAssembly("Mods/second.dll", "Same.Assembly", "Example Mod", "2.0.0", "example.mod", new Version(2, 0));

        var all = files.Scan();

        Assert.Equal(2, all.Components.Length);
        Assert.Equal(2, all.Components.Select(component => component.Key).Distinct().Count());
        var known = all.Components[0];
        var supplemented = files.Scan(known: new[] { known });
        Assert.Single(supplemented.Components);
        Assert.All(supplemented.Components, component => Assert.Equal(DiagnosticLoadState.Present, component.State));
    }

    [Theory]
    [InlineData(7, "", "2.3.4")]
    [InlineData(8, "-beta.1", "2.3.4-beta.1")]
    public void Numeric_version_metadata_enriches_failed_registry_rows_without_changing_identity(int parameters,
        string identifier, string expectedVersion)
    {
        using var files = new Fixture();
        files.WriteAssembly("Mods/numeric.dll", "Fixture.Numeric", "Numeric Mod", "2.3.4", "numeric.mod",
            numericParameters: parameters, numericIdentifier: identifier);
        string path = files.Path("Mods/numeric.dll");
        var known = new DiagnosticComponent
        {
            Key = "original-rotten-key", Path = path, Kind = DiagnosticComponentKind.Mod, State = DiagnosticLoadState.Failed,
            Name = "Example.Core", TypeName = "Example.Core", Version = "",
        };

        var enriched = Assert.Single(files.Scan(known: new[] { known }).Components);

        Assert.Equal("original-rotten-key", enriched.Key);
        Assert.Equal(DiagnosticLoadState.Failed, enriched.State);
        Assert.Equal("Numeric Mod", enriched.Name);
        Assert.Equal("numeric.mod", enriched.Id);
        Assert.Equal(expectedVersion, enriched.Version);
    }

    [Fact]
    public void Invalid_metadata_is_an_evidence_problem_not_a_load_failure()
    {
        using var files = new Fixture();
        files.Write("Mods/broken.dll", "invalid portable executable", Now);

        var result = files.Scan();

        Assert.Equal(DiagnosticLoadState.Present, Assert.Single(result.Components).State);
        Assert.All(result.Issues, issue => Assert.Equal(DiagnosticIssueKind.EvidenceUnavailable, issue.Kind));
    }

    private static DiagnosticComponent Component(string path, DiagnosticLoadState state = DiagnosticLoadState.Present) => new()
    {
        Key = NewsDiagnosticIdentity.ComponentKey(DiagnosticComponentKind.Mod, path, typeName: "Example.Core"),
        Name = "Example Mod",
        Id = "example.mod",
        Version = "1.0.0",
        Path = path,
        TypeName = "Example.Core",
        Kind = DiagnosticComponentKind.Mod,
        State = state,
    };

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Anomaly-News-" + Guid.NewGuid().ToString("N"));
        internal Fixture()
        {
            Directory.CreateDirectory(Root);
            foreach (string child in new[] { "Logs", "Reports", "Mods", "Plugins", "UserLibs" })
                Directory.CreateDirectory(Path(child));
        }
        internal string Path(string relative) => System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        internal void Write(string relative, string text, DateTimeOffset timestamp)
        {
            string path = Path(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            File.SetLastWriteTimeUtc(path, timestamp.UtcDateTime);
        }
        internal void WriteReport(string relative, string trigger, DateTimeOffset timestamp, string message, string type = "") =>
            Write(relative, JsonSerializer.Serialize(new { Timestamp = timestamp, TriggerType = trigger, ExceptionType = type,
                ExceptionMessage = message, LoadedMods = new[] { "Example Mod v1.0.0" } }), timestamp);
        internal NewsHistoryResult Read(DateTimeOffset started, Func<string, string>? redact = null) =>
            NewsHistoryReader.Read(Path("Latest.log"), Path("Logs"), Path("Reports"), started, Marker,
                Array.Empty<DiagnosticComponent>(), redact, Now);
        internal NewsInventoryResult Scan(bool disableSubfolders = false, bool disableManifest = false,
            IReadOnlyList<DiagnosticComponent>? known = null) => NewsMetadataInventory.Scan(Path("Mods"), Path("Plugins"),
                Path("UserLibs"), disableSubfolders, disableManifest, known ?? Array.Empty<DiagnosticComponent>());
        internal void WriteAssembly(string relative, string assemblyName, string name, string version, string id,
            Version? assemblyVersion = null, int numericParameters = 0, string numericIdentifier = "")
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString(assemblyName + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
            var assembly = metadata.AddAssembly(metadata.GetOrAddString(assemblyName), assemblyVersion ?? new Version(1, 0, 0, 0),
                default, default, (AssemblyFlags)0, AssemblyHashAlgorithm.None);
            metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var infoType = metadata.AddTypeReference(default, metadata.GetOrAddString("MelonLoader"), metadata.GetOrAddString("MelonInfoAttribute"));
            var idType = metadata.AddTypeReference(default, metadata.GetOrAddString("MelonLoader"), metadata.GetOrAddString("MelonIDAttribute"));
            var signature = metadata.GetOrAddBlob(new byte[] { 0x20, (byte)(numericParameters == 0 ? 5 : numericParameters), 1 });
            var infoConstructor = metadata.AddMemberReference(infoType, metadata.GetOrAddString(".ctor"), signature);
            var idConstructor = metadata.AddMemberReference(idType, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(new byte[] { 0x20, 1, 1 }));
            var info = new BlobBuilder();
            info.WriteUInt16(1);
            info.WriteSerializedString("Example.Core, " + assemblyName);
            info.WriteSerializedString(name);
            if (numericParameters == 0) info.WriteSerializedString(version);
            else
            {
                var numeric = Version.Parse(version);
                info.WriteInt32(numeric.Major);
                info.WriteInt32(numeric.Minor);
                info.WriteInt32(numeric.Build);
                if (numericParameters == 8) info.WriteSerializedString(numericIdentifier);
            }
            info.WriteSerializedString("Fixture");
            info.WriteSerializedString(null);
            info.WriteUInt16(0);
            var identifier = new BlobBuilder();
            identifier.WriteUInt16(1);
            identifier.WriteSerializedString(id);
            identifier.WriteUInt16(0);
            metadata.AddCustomAttribute(assembly, infoConstructor, metadata.GetOrAddBlob(info));
            metadata.AddCustomAttribute(assembly, idConstructor, metadata.GetOrAddBlob(identifier));
            var pe = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
                new MetadataRootBuilder(metadata), new BlobBuilder());
            var output = new BlobBuilder();
            pe.Serialize(output);
            string path = Path(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, output.ToArray());
        }
        public void Dispose()
        {
            // The generated, unique fixture root is the only recursively deleted target.
            string resolved = System.IO.Path.GetFullPath(Root);
            string temp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(resolved).StartsWith("Anomaly-News-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected fixture root.");
            Directory.Delete(resolved, recursive: true);
        }
    }
}
