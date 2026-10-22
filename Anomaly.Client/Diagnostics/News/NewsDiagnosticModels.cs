#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Anomaly.Client.Diagnostics.News;

internal enum DiagnosticComponentKind { Mod, Plugin, Dependency, ManagedModule }
internal enum DiagnosticLoadState { Unknown, Present, Registered, Loaded, Failed, Disabled }
internal enum DiagnosticIssueKind
{
    LoadFailure, InitializationError, MissingDependency, ReportedError, FeatureHealth,
    DiagnosticReport, EvidenceUnavailable, InterruptedActivity,
}
internal enum DiagnosticSeverity { Info, Warning, Error }

internal sealed class DiagnosticComponent
{
    internal string Key { get; init; } = string.Empty;
    internal string Name { get; init; } = string.Empty;
    internal string Version { get; init; } = string.Empty;
    internal string Id { get; init; } = string.Empty;
    internal string Path { get; init; } = string.Empty;
    internal string TypeName { get; init; } = string.Empty;
    internal DiagnosticComponentKind Kind { get; init; }
    internal DiagnosticLoadState State { get; init; }
}

internal sealed class DiagnosticIssue
{
    internal string Key { get; init; } = string.Empty;
    internal string ComponentKey { get; init; } = string.Empty;
    internal string ComponentName { get; init; } = string.Empty;
    internal string ComponentId { get; init; } = string.Empty;
    internal string ComponentPath { get; init; } = string.Empty;
    internal string Summary { get; init; } = string.Empty;
    internal string Source { get; init; } = string.Empty;
    internal string Code { get; init; } = string.Empty;
    internal string DependencyName { get; init; } = string.Empty;
    internal string DependencyVersion { get; init; } = string.Empty;
    internal DiagnosticIssueKind Kind { get; init; }
    internal DiagnosticSeverity Severity { get; init; }
    internal DateTimeOffset? Timestamp { get; init; }
    internal bool IsHistorical { get; init; }
}

internal sealed class DiagnosticValue
{
    internal string Key { get; init; } = string.Empty;
    internal string Value { get; init; } = string.Empty;
}

internal sealed class NewsDiagnosticSnapshot
{
    internal int Revision { get; init; }
    internal bool IsCollecting { get; init; }
    internal string GameVersion { get; init; } = string.Empty;
    internal string AnomalyVersion { get; init; } = string.Empty;
    internal string LoaderVersion { get; init; } = string.Empty;
    internal DiagnosticComponent[] Components { get; init; } = Array.Empty<DiagnosticComponent>();
    internal DiagnosticIssue[] CurrentIssues { get; init; } = Array.Empty<DiagnosticIssue>();
    internal DiagnosticIssue[] History { get; init; } = Array.Empty<DiagnosticIssue>();
    internal DiagnosticValue[] Runtime { get; init; } = Array.Empty<DiagnosticValue>();
}

internal sealed class NewsInventoryResult
{
    internal DiagnosticComponent[] Components { get; init; } = Array.Empty<DiagnosticComponent>();
    internal DiagnosticIssue[] Issues { get; init; } = Array.Empty<DiagnosticIssue>();
}

internal sealed class NewsHistoryResult
{
    internal DiagnosticIssue[] CurrentIssues { get; init; } = Array.Empty<DiagnosticIssue>();
    internal DiagnosticIssue[] History { get; init; } = Array.Empty<DiagnosticIssue>();
}

internal static class NewsDiagnosticIdentity
{
    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return System.IO.Path.GetFullPath(path.Replace('\\', System.IO.Path.DirectorySeparatorChar))
            .Replace('\\', '/').TrimEnd('/'); }
        catch { return string.Empty; }
    }

    internal static string ComponentKey(DiagnosticComponentKind kind, string path,
        string id = null, string typeName = null) =>
        "component:" + kind + ":" + Hash(NormalizePath(path).ToUpperInvariant() + "|" +
            (kind == DiagnosticComponentKind.ManagedModule ? id : typeName));

    internal static string IssueKey(DiagnosticIssueKind kind, string source, string componentKey,
        string code, string message) => "issue:" + Hash(kind + "|" + source + "|" + componentKey + "|" + code + "|" + message);

    internal static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    internal static DiagnosticComponent Match(IReadOnlyList<DiagnosticComponent> components,
        string path = null, string name = null, string id = null, string section = null)
    {
        if (components == null) return null;
        var candidates = components.Where(component => component != null).ToArray();
        if (!string.IsNullOrWhiteSpace(path))
        {
            string normalized = NormalizePath(path);
            if (normalized.Length != 0)
            {
                var matches = candidates.Where(component => string.Equals(NormalizePath(component.Path), normalized,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 1) return matches[0];
            }
            // A supplied source path identifies the observed file. Missing or ambiguous
            // paths must not fall back to a different file with the same display name.
            return null;
        }
        if (!string.IsNullOrWhiteSpace(id))
        {
            var matches = candidates.Where(component => string.Equals(component.Id, id, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 1) return matches[0];
            if (matches.Length > 1) return null;
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            var matches = candidates.Where(component => string.Equals(component.Name, name, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 1) return matches[0];
            if (matches.Length > 1) return null;
        }
        if (!string.IsNullOrWhiteSpace(section))
        {
            var matches = candidates.Where(component => string.Equals(section, component.Name, StringComparison.Ordinal) ||
                string.Equals(section, component.Name.Replace(' ', '_'), StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(component.Id) && string.Equals(section,
                    (component.Id + ":" + component.Name).Replace(' ', '_'), StringComparison.Ordinal)).ToArray();
            if (matches.Length == 1) return matches[0];
        }
        return null;
    }

    internal static string SafeText(string text, Func<string, string> redact, int limit = 768)
    {
        string bounded = (text ?? string.Empty);
        if (bounded.Length > 8192) bounded = bounded.Substring(0, 8192);
        try { bounded = redact == null ? bounded : redact(bounded) ?? string.Empty; }
        catch { return "Diagnostic detail unavailable."; }
        bounded = new string(bounded.Where(character => !char.IsControl(character) || character == '\n' || character == '\t').ToArray());
        return bounded.Length <= limit ? bounded : bounded.Substring(0, limit) + " [truncated]";
    }

    internal static DiagnosticIssue Unavailable(string source, string code, string summary,
        bool historical, DateTimeOffset? timestamp = null) => new()
    {
        Key = IssueKey(DiagnosticIssueKind.EvidenceUnavailable, source, string.Empty, code, summary),
        Kind = DiagnosticIssueKind.EvidenceUnavailable,
        Severity = DiagnosticSeverity.Warning,
        Source = source,
        Code = code,
        Summary = summary,
        Timestamp = timestamp,
        IsHistorical = historical,
    };
}
