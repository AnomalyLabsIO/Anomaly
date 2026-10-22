#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Anomaly.Client.Diagnostics.News;

/// <summary>Imports bounded evidence; timestamps and report kinds are never treated as crash attribution.</summary>
internal static class NewsHistoryReader
{
    private const int MaximumCurrentLogBytes = 4 * 1024 * 1024;
    private const int MaximumReportBytes = 1024 * 1024;
    private const int MaximumDirectoryEntries = 512;
    private const int MaximumCurrentArchiveCandidates = 16;
    private const int MaximumPreviousLogs = 3;
    private const int MaximumReports = 20;
    private static readonly TimeSpan HistoryAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan ReportAge = TimeSpan.FromDays(30);
    private static readonly Regex SessionMarker = new(@"(?m)^(?:\[\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?\][ \t]*)?(?:\[[^\]\r\n]{1,256}\][ \t]*)*(?<marker>ANOMALY-NEWS-SESSION [a-fA-F0-9-]{32,36})[ \t]*\r?$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static NewsHistoryResult Read(string latestLogPath, string archivedLogsDirectory, string reportsDirectory,
        DateTimeOffset processStartedUtc, string sessionMarker, IReadOnlyList<DiagnosticComponent> components,
        Func<string, string> redact = null, DateTimeOffset? now = null)
    {
        DateTimeOffset nowUtc = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var current = new List<DiagnosticIssue>();
        var history = new List<DiagnosticIssue>();
        var latest = ReadText(latestLogPath, MaximumCurrentLogBytes);
        var archived = Enumerate(archivedLogsDirectory, "*.log", current, historical: false)
            .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        TextEvidence selected = null;
        string selectedPath = null;
        int checkedCurrent = 0;
        foreach (var archive in archived.Where(file => file.LastWriteTimeUtc >= processStartedUtc.UtcDateTime))
        {
            if (checkedCurrent++ >= MaximumCurrentArchiveCandidates)
            {
                current.Add(NewsDiagnosticIdentity.Unavailable("logs", "current-log-search-limit",
                    "Additional current log archives were omitted after the search limit.", false));
                break;
            }
            var evidence = ReadText(archive.FullName, MaximumCurrentLogBytes);
            if (!ContainsOnlySession(evidence.Text, sessionMarker)) continue;
            selected = evidence;
            selectedPath = archive.FullName;
            break;
        }
        if (selected == null && ContainsOnlySession(latest.Text, sessionMarker))
        {
            selected = latest;
            selectedPath = latestLogPath;
        }
        if (selected == null)
        {
            current.Add(NewsDiagnosticIdentity.Unavailable("logs", "current-log-unverified",
                "Current log evidence is unavailable or could not be verified for this launch.", false));
        }
        else
        {
            AddReadIssue(selected, selectedPath, current, historical: false);
            // The service subscribes logger callbacks before emitting this marker. Import only
            // earlier startup evidence, so the same later observation is not counted twice.
            int markerLine = SessionMarker.Matches(selected.Text).Cast<Match>()
                .First(match => match.Groups["marker"].Value == sessionMarker).Index;
            current.AddRange(NewsLogParser.Parse(selected.Text.Substring(0, markerLine), components, Path.GetFileName(selectedPath),
                historical: false, redact: redact));
        }

        int previousCount = 0;
        var previousPrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var archive in archived.Where(file => file.LastWriteTimeUtc < processStartedUtc.UtcDateTime &&
                     file.LastWriteTimeUtc >= (nowUtc - HistoryAge).UtcDateTime))
        {
            if (previousCount >= MaximumPreviousLogs) break;
            var evidence = ReadText(archive.FullName, MaximumCurrentLogBytes);
            if (HasSession(evidence.Text, sessionMarker)) continue;
            // A copied Latest.log may not retain a session marker at the bounded edge. Equal
            // prefixes are still duplicate evidence, rather than another previous launch.
            if (!string.IsNullOrEmpty(selected?.Text) && SamePrefix(evidence.Text, selected.Text)) continue;
            string fingerprint = NewsDiagnosticIdentity.Hash(Prefix(evidence.Text));
            if (evidence.Text != null && !previousPrefixes.Add(fingerprint)) continue;
            previousCount++;
            AddReadIssue(evidence, archive.FullName, history, historical: true, archive.LastWriteTimeUtc);
            if (evidence.Text != null)
                history.AddRange(NewsLogParser.Parse(evidence.Text, components, Path.GetFileName(archive.FullName),
                    archive.LastWriteTimeUtc, historical: true, redact: redact));
        }

        var reportCandidates = Enumerate(reportsDirectory, "crash-*", history, historical: true)
            .Where(file => (file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                            file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) &&
                           file.LastWriteTimeUtc >= (nowUtc - ReportAge).UtcDateTime)
            .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        var reports = reportCandidates.Take(MaximumReports).ToArray();
        if (reportCandidates.Length > MaximumReports)
            history.Add(NewsDiagnosticIdentity.Unavailable("reports", "report-limit",
                "Additional retained diagnostic reports were omitted after the report limit.", true));
        foreach (var report in reports)
        {
            var evidence = ReadText(report.FullName, MaximumReportBytes);
            bool historical = report.LastWriteTimeUtc < processStartedUtc.UtcDateTime;
            var destination = historical ? history : current;
            AddReadIssue(evidence, report.FullName, destination, historical, report.LastWriteTimeUtc);
            if (evidence.Text == null || evidence.Truncated) continue;
            try
            {
                var issue = report.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                    ? ReadJsonReport(evidence.Text, report.Name, processStartedUtc, nowUtc, redact)
                    : ReadNativeReport(evidence.Text, report.Name, processStartedUtc, nowUtc);
                if (issue != null) (issue.IsHistorical ? history : current).Add(issue);
            }
            catch (Exception exception) when (exception is JsonException || exception is FormatException ||
                                              exception is InvalidDataException || exception is InvalidOperationException)
            {
                destination.Add(NewsDiagnosticIdentity.Unavailable(report.Name, "report-invalid",
                    "Diagnostic report metadata could not be read (" + exception.GetType().Name + ").", historical,
                    report.LastWriteTimeUtc));
            }
        }
        return new NewsHistoryResult
        {
            CurrentIssues = Unique(current),
            History = Unique(history),
        };
    }

    internal static DiagnosticIssue ReadCleanShutdownMarker(string markerPath, Func<string, string> redact = null)
    {
        var evidence = ReadText(markerPath, 4096);
        string source = Path.GetFileName(markerPath);
        if (evidence.Text == null || evidence.Truncated ||
            !DateTimeOffset.TryParse(evidence.Text.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var timestamp))
            return NewsDiagnosticIdentity.Unavailable(source, "clean-shutdown-unavailable",
                "No valid clean shutdown record is available; the previous exit cause is unknown.", true);
        return new DiagnosticIssue
        {
            Key = NewsDiagnosticIdentity.IssueKey(DiagnosticIssueKind.DiagnosticReport, source, null, "clean-shutdown", timestamp.ToString("O")),
            Kind = DiagnosticIssueKind.DiagnosticReport,
            Severity = DiagnosticSeverity.Info,
            Source = source,
            Code = "clean-shutdown",
            Summary = "A clean Anomaly shutdown was recorded at this time.",
            Timestamp = timestamp,
            IsHistorical = true,
        };
    }

    private static DiagnosticIssue ReadJsonReport(string text, string source, DateTimeOffset processStartedUtc,
        DateTimeOffset nowUtc, Func<string, string> redact)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Timestamp", out var date) ||
            date.ValueKind != JsonValueKind.String || !date.TryGetDateTimeOffset(out var timestamp))
            throw new InvalidDataException("Report timestamp is unavailable.");
        if (timestamp < nowUtc - ReportAge) return null;
        if (timestamp > nowUtc + TimeSpan.FromMinutes(5)) throw new InvalidDataException("Report timestamp is in the future.");
        string trigger = JsonString(root, "TriggerType");
        if (string.IsNullOrWhiteSpace(trigger)) throw new InvalidDataException("Report trigger is unavailable.");
        string label = trigger switch
        {
            "HangDetected" => "Hang diagnostic report",
            "FirstChanceFlood" => "Repeated-exception diagnostic report",
            "TaskException" => "Task exception diagnostic report",
            "UnityException" => "Unity exception diagnostic report",
            "UnityAssert" => "Unity assertion diagnostic report",
            "UnhandledException" => "Unhandled exception diagnostic report",
            "NativeCrash" => "Native crash diagnostic report",
            _ => "Diagnostic report",
        };
        string exceptionType = JsonString(root, "ExceptionType");
        string message = JsonString(root, "ExceptionMessage");
        string summary = label + (exceptionType.Length == 0 ? string.Empty : ": " + exceptionType) +
            (message.Length == 0 ? string.Empty : " — " + message);
        return new DiagnosticIssue
        {
            Key = NewsDiagnosticIdentity.IssueKey(DiagnosticIssueKind.DiagnosticReport, source, null, trigger, timestamp.ToString("O")),
            Kind = DiagnosticIssueKind.DiagnosticReport,
            Severity = trigger == "NativeCrash" || trigger == "UnhandledException" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            Source = source,
            Code = NewsDiagnosticIdentity.SafeText(trigger, redact, 128),
            Summary = NewsDiagnosticIdentity.SafeText(summary, redact),
            Timestamp = timestamp,
            IsHistorical = timestamp < processStartedUtc,
        };
    }

    private static DiagnosticIssue ReadNativeReport(string text, string source, DateTimeOffset processStartedUtc,
        DateTimeOffset nowUtc)
    {
        string dateLine = text.Split('\n').FirstOrDefault(line => line.StartsWith("Native crash at ", StringComparison.Ordinal));
        if (dateLine == null || !DateTimeOffset.TryParse(dateLine.Substring("Native crash at ".Length).Trim(),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            throw new InvalidDataException("Native report timestamp is unavailable.");
        if (timestamp < nowUtc - ReportAge) return null;
        if (timestamp > nowUtc + TimeSpan.FromMinutes(5)) throw new InvalidDataException("Native report timestamp is in the future.");
        return new DiagnosticIssue
        {
            Key = NewsDiagnosticIdentity.IssueKey(DiagnosticIssueKind.DiagnosticReport, source, null, "NativeCrash", timestamp.ToString("O")),
            Kind = DiagnosticIssueKind.DiagnosticReport,
            Severity = DiagnosticSeverity.Error,
            Source = source,
            Code = "NativeCrash",
            Summary = "A native crash diagnostic artifact was recorded; component attribution is unavailable.",
            Timestamp = timestamp,
            IsHistorical = timestamp < processStartedUtc,
        };
    }

    private static string JsonString(JsonElement root, string name) => root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static FileInfo[] Enumerate(string directory, string pattern, List<DiagnosticIssue> issues, bool historical)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return Array.Empty<FileInfo>();
        var result = new List<FileInfo>();
        try
        {
            foreach (string path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                if (result.Count >= MaximumDirectoryEntries)
                {
                    issues.Add(NewsDiagnosticIdentity.Unavailable("evidence", "evidence-directory-limit",
                        "Additional evidence files were omitted after the directory limit.", historical));
                    break;
                }
                var file = new FileInfo(path);
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0) result.Add(file);
            }
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                          exception is System.Security.SecurityException)
        {
            issues.Add(NewsDiagnosticIdentity.Unavailable("evidence", "evidence-directory-unavailable",
                "The evidence directory could not be inspected (" + exception.GetType().Name + ").", historical));
        }
        return result.ToArray();
    }

    private sealed class TextEvidence
    {
        internal string Text;
        internal string Error;
        internal bool Truncated;
    }

    private static TextEvidence ReadText(string path, int maximumBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return new TextEvidence { Error = "ReparsePoint" };
            var bytes = new byte[(int)Math.Min(stream.Length, maximumBytes)];
            int read = 0;
            while (read < bytes.Length)
            {
                int count = stream.Read(bytes, read, bytes.Length - read);
                if (count == 0) break;
                read += count;
            }
            bool truncated = stream.Length > maximumBytes || stream.ReadByte() != -1;
            string text = Encoding.UTF8.GetString(bytes, 0, read).TrimStart('\uFEFF');
            return new TextEvidence { Text = text, Truncated = truncated };
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                          exception is ArgumentException || exception is System.Security.SecurityException)
        {
            return new TextEvidence { Error = exception.GetType().Name };
        }
    }

    private static void AddReadIssue(TextEvidence evidence, string path, List<DiagnosticIssue> issues,
        bool historical, DateTimeOffset? timestamp = null)
    {
        string source = Path.GetFileName(path);
        if (evidence.Text == null)
            issues.Add(NewsDiagnosticIdentity.Unavailable(source, "evidence-file-unavailable",
                "Evidence file could not be read (" + evidence.Error + ").", historical, timestamp));
        else if (evidence.Truncated)
            issues.Add(NewsDiagnosticIdentity.Unavailable(source, "evidence-truncated",
                "Evidence exceeds the read limit; only its beginning was inspected.", historical, timestamp));
    }

    private static bool ContainsOnlySession(string text, string expected)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(expected)) return false;
        var matches = SessionMarker.Matches(text).Cast<Match>().Select(match => match.Groups["marker"].Value).Distinct(StringComparer.Ordinal).ToArray();
        return matches.Length == 1 && string.Equals(matches[0], expected, StringComparison.Ordinal);
    }

    private static bool HasSession(string text, string expected) => !string.IsNullOrEmpty(text) &&
        !string.IsNullOrEmpty(expected) && SessionMarker.Matches(text).Cast<Match>().Any(match => match.Groups["marker"].Value == expected);

    private static string Prefix(string text) => text == null ? string.Empty : text.Substring(0, Math.Min(text.Length, 4096));
    private static bool SamePrefix(string first, string second) => !string.IsNullOrEmpty(first) &&
        !string.IsNullOrEmpty(second) && Prefix(first) == Prefix(second);
    private static DiagnosticIssue[] Unique(IEnumerable<DiagnosticIssue> issues) => issues.GroupBy(issue => issue.Key,
        StringComparer.Ordinal).Select(group => group.First()).ToArray();
}
