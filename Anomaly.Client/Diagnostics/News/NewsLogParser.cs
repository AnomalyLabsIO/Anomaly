#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Anomaly.Client.Diagnostics.News;

/// <summary>Recognizes loader evidence without treating stack membership or log order as fault attribution.</summary>
internal static class NewsLogParser
{
    private const int MaximumIssues = 512;
    private const int MaximumMessageCharacters = 8192;
    private static readonly TimeSpan RegexBudget = TimeSpan.FromMilliseconds(100);
    private static readonly Regex TimestampPrefix = Pattern(@"^\[\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?\]\s*(?<text>.*)$");
    private static readonly Regex Tag = Pattern(@"^\[(?<tag>[^\]\r\n]{1,256})\]\s*(?<text>.*)$");
    private static readonly Regex AssemblyFailure = Pattern(@"^Failed to load (?<metadata>AssemblyDefinition|Melon Assembly) from '(?<path>[^'\r\n]+)':");
    private static readonly Regex RegistrationFailure = Pattern(@"^Failed to register (?:Mod|Plugin) '(?<path>[^'\r\n]+)': (?<reason>.*)$");
    private static readonly Regex MelonFailure = Pattern(@"^Failed to load Melon '(?<name>[^'\r\n]+)'(?: from '(?<path>[^'\r\n]+)')?:\s*(?<reason>.*)$");
    private static readonly Regex MissingOwner = Pattern(@"^- '(?<name>[^'\r\n]+)' is missing the following dependencies:$");
    private static readonly Regex MissingDependency = Pattern(@"^\s+- '(?<name>[^'\r\n]+)' v(?<version>[^\r\n]+)$");
    private static readonly Regex QuotedOwner = Pattern(@"^- '(?<name>[^'\r\n]+)'(?: is incompatible with the following Melons:)?$");
    private static readonly Regex IncompatibleOwner = Pattern(@"^'(?<name>[^'\r\n]+) v[^'\r\n]+' is incompatible:$");

    internal static DiagnosticIssue[] Parse(string text, IReadOnlyList<DiagnosticComponent> components,
        string source, DateTimeOffset? sourceTimestamp = null, bool historical = false,
        Func<string, string> redact = null)
    {
        var issues = new List<DiagnosticIssue>();
        using var reader = new StringReader(text ?? string.Empty);
        var message = new StringBuilder();
        string section = string.Empty;
        DiagnosticSeverity severity = DiagnosticSeverity.Info;
        bool ignored = false;
        bool hasBlock = false;
        bool omitted = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Flush()
        {
            if (!hasBlock || ignored || message.Length == 0) return;
            foreach (var issue in ParseMessage(severity, section, message.ToString(), components,
                         source, sourceTimestamp, historical, redact))
            {
                if (!seen.Add(issue.Key)) continue;
                if (issues.Count >= MaximumIssues) { omitted = true; return; }
                issues.Add(issue);
            }
        }

        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var prefix = TimestampPrefix.Match(line);
            if (prefix.Success)
            {
                Flush();
                message.Clear();
                hasBlock = true;
                ignored = false;
                section = string.Empty;
                severity = DiagnosticSeverity.Info;
                string body = prefix.Groups["text"].Value;
                var firstTag = Tag.Match(body);
                if (firstTag.Success)
                {
                    string first = firstTag.Groups["tag"].Value;
                    if (first == "DEBUG" || first == "BS DEBUG") ignored = true;
                    if (first == "ERROR" || first == "WARNING")
                    {
                        severity = first == "ERROR" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
                        body = firstTag.Groups["text"].Value;
                        var ownerTag = Tag.Match(body);
                        if (ownerTag.Success)
                        {
                            section = ownerTag.Groups["tag"].Value;
                            body = ownerTag.Groups["text"].Value;
                        }
                    }
                    else
                    {
                        section = first;
                        body = firstTag.Groups["text"].Value;
                    }
                }
                message.Append(body.AsSpan(0, Math.Min(body.Length, MaximumMessageCharacters)));
            }
            else if (hasBlock && message.Length < MaximumMessageCharacters)
            {
                message.Append('\n');
                message.Append(line.AsSpan(0, Math.Min(line.Length, MaximumMessageCharacters - message.Length)));
            }
        }
        Flush();
        if (omitted)
            issues.Add(NewsDiagnosticIdentity.Unavailable(source, "issue-limit",
                "Additional log issues were omitted after the diagnostic limit.", historical, sourceTimestamp));
        return issues.ToArray();
    }

    internal static DiagnosticIssue[] ParseMessage(DiagnosticSeverity severity, string section, string message,
        IReadOnlyList<DiagnosticComponent> components, string source, DateTimeOffset? timestamp = null,
        bool historical = false, Func<string, string> redact = null)
    {
        if (section == "DEBUG" || section == "BS DEBUG" || string.IsNullOrWhiteSpace(message))
            return Array.Empty<DiagnosticIssue>();
        string bounded = message.Length <= MaximumMessageCharacters ? message : message.Substring(0, MaximumMessageCharacters);
        string firstLine = bounded.Split('\n')[0].TrimEnd('\r');
        if (severity == DiagnosticSeverity.Warning && message.Length <= MaximumMessageCharacters &&
            NewsDiagnosticPolicy.IsExpectedWarning(section, bounded.TrimEnd()))
            return Array.Empty<DiagnosticIssue>();
        if (severity == DiagnosticSeverity.Warning &&
            (firstLine.StartsWith("Diagnostics:", StringComparison.Ordinal) ||
             firstLine.StartsWith("Diagnostic artifact written locally:", StringComparison.Ordinal)))
            return Array.Empty<DiagnosticIssue>();
        var issues = new List<DiagnosticIssue>();
        void Add(DiagnosticIssueKind kind, DiagnosticSeverity level, string code, string summary,
            string path = null, string name = null, string id = null, string dependencyName = null, string dependencyVersion = null)
        {
            var component = NewsDiagnosticIdentity.Match(components, path, name, id, section);
            string safeSummary = NewsDiagnosticIdentity.SafeText(summary, redact);
            string displayName = component?.Name ?? name ?? string.Empty;
            if (displayName.Length == 0 && !string.IsNullOrEmpty(path))
                displayName = Path.GetFileName(path.Replace('\\', '/'));
            issues.Add(new DiagnosticIssue
            {
                Key = NewsDiagnosticIdentity.IssueKey(kind, source, component?.Key, code, bounded + "|" + name + "|" + summary),
                ComponentKey = component?.Key ?? string.Empty,
                ComponentName = displayName,
                ComponentId = component?.Id ?? id ?? string.Empty,
                ComponentPath = component?.Path ?? (path == null ? string.Empty : NewsDiagnosticIdentity.NormalizePath(path)),
                Summary = safeSummary,
                Kind = kind,
                Severity = level,
                Source = source ?? string.Empty,
                Code = code,
                DependencyName = dependencyName ?? string.Empty,
                DependencyVersion = dependencyVersion ?? string.Empty,
                Timestamp = timestamp,
                IsHistorical = historical,
            });
        }

        if (firstLine.StartsWith("Some Melons are missing dependencies,", StringComparison.Ordinal))
        {
            string owner = null;
            foreach (string line in bounded.Split('\n'))
            {
                var ownerMatch = MissingOwner.Match(line.TrimEnd('\r'));
                if (ownerMatch.Success) { owner = ownerMatch.Groups["name"].Value; continue; }
                var dependency = MissingDependency.Match(line.TrimEnd('\r'));
                if (owner != null && dependency.Success &&
                    Regex.IsMatch(dependency.Groups["name"].Value, @"\A[A-Za-z_][A-Za-z0-9_.+-]{0,127}\z", RegexOptions.CultureInvariant, RegexBudget) &&
                    Version.TryParse(dependency.Groups["version"].Value, out _))
                    Add(DiagnosticIssueKind.MissingDependency, DiagnosticSeverity.Warning, "missing-dependency",
                        "Missing dependency warning: " + dependency.Groups["name"].Value + " v" + dependency.Groups["version"].Value,
                        name: owner, dependencyName: dependency.Groups["name"].Value, dependencyVersion: dependency.Groups["version"].Value);
            }
            if (issues.Count == 0)
                Add(DiagnosticIssueKind.MissingDependency, DiagnosticSeverity.Warning, "missing-dependency", firstLine);
            return issues.ToArray();
        }
        if (firstLine.StartsWith("Some Melons could not be loaded due to a cyclic dependency:", StringComparison.Ordinal) ||
            firstLine.StartsWith("Some Melons are marked as incompatible with each other.", StringComparison.Ordinal))
        {
            bool cyclic = firstLine.StartsWith("Some Melons could not", StringComparison.Ordinal);
            foreach (string line in bounded.Split('\n'))
            {
                var match = QuotedOwner.Match(line.TrimEnd('\r'));
                if (match.Success)
                    Add(DiagnosticIssueKind.LoadFailure, DiagnosticSeverity.Error,
                        cyclic ? "dependency-cycle" : "incompatible-components",
                        cyclic ? "Loader rejected a cyclic dependency." : "Loader reported incompatible components.",
                        name: match.Groups["name"].Value);
            }
            if (issues.Count == 0)
                Add(DiagnosticIssueKind.LoadFailure, DiagnosticSeverity.Error,
                    cyclic ? "dependency-cycle" : "incompatible-components", firstLine);
            return issues.ToArray();
        }
        var assembly = AssemblyFailure.Match(firstLine);
        if (assembly.Success)
        {
            Add(DiagnosticIssueKind.LoadFailure, DiagnosticSeverity.Error,
                assembly.Groups["metadata"].Value == "AssemblyDefinition" ? "assembly-metadata-failed" : "assembly-load-failed",
                bounded, path: assembly.Groups["path"].Value);
            return issues.ToArray();
        }
        var registration = RegistrationFailure.Match(firstLine);
        if (registration.Success)
        {
            bool initialization = registration.Groups["reason"].Value.StartsWith("Melon failed to initialize!", StringComparison.Ordinal);
            Add(initialization ? DiagnosticIssueKind.InitializationError : DiagnosticIssueKind.LoadFailure,
                severity == DiagnosticSeverity.Warning ? severity : DiagnosticSeverity.Error,
                initialization ? "registration-initialization-failed" : "registration-failed", bounded,
                path: registration.Groups["path"].Value);
            return issues.ToArray();
        }
        var melon = MelonFailure.Match(firstLine);
        if (melon.Success)
        {
            Add(DiagnosticIssueKind.LoadFailure, DiagnosticSeverity.Error, "melon-load-failed", bounded,
                path: melon.Groups["path"].Success ? melon.Groups["path"].Value : null,
                name: melon.Groups["name"].Value);
            return issues.ToArray();
        }
        var incompatible = IncompatibleOwner.Match(firstLine);
        if (incompatible.Success)
        {
            Add(DiagnosticIssueKind.LoadFailure, DiagnosticSeverity.Error, "loader-incompatible", firstLine,
                name: incompatible.Groups["name"].Value);
            return issues.ToArray();
        }
        if (severity == DiagnosticSeverity.Error || severity == DiagnosticSeverity.Warning)
        {
            if (severity == DiagnosticSeverity.Error && NewsDiagnosticIdentity.Match(components, section: section) != null &&
                Regex.IsMatch(bounded, @"(?m)^\s*at MelonLoader\.MelonBase\.LoaderInitialized\(", RegexOptions.CultureInvariant, RegexBudget))
            {
                Add(DiagnosticIssueKind.InitializationError, DiagnosticSeverity.Error, "melon-initialization-error", bounded);
                return issues.ToArray();
            }
            bool dependencyFailure = firstLine.StartsWith("Loading Melon Dependency Failed:", StringComparison.Ordinal) ||
                firstLine.StartsWith("Resolving Melon Dependency Failed:", StringComparison.Ordinal);
            Add(dependencyFailure ? DiagnosticIssueKind.MissingDependency : DiagnosticIssueKind.ReportedError,
                severity, dependencyFailure ? "dependency-load-error" : severity == DiagnosticSeverity.Error ? "reported-error" : "reported-warning",
                bounded);
        }
        return issues.ToArray();
    }

    private static Regex Pattern(string value) => new(value, RegexOptions.CultureInvariant, RegexBudget);
}
