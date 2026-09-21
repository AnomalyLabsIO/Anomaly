using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Anomaly.Client.Api.Modding;

namespace Anomaly.Client.Api.Internal.Diagnostics;

/// <summary>A deliberately small diagnostic projection; free-form health details stay in local logs.</summary>
internal static class StatusReport
{
    private static readonly HashSet<string> CoreFeatures = new(StringComparer.Ordinal)
    {
        "localization", "offline-auth-mode", "mandatory-patches", "authentication", "capabilities",
        "motion", "runtime-hooks", "diagnostics", "ui", "preloader-skin", "main-menu", "mod-settings",
        "managed-mods", "commands", "networking", "content", "encrypted-channel-byte-send",
    };
    internal static string Format(Version coreVersion, Version apiVersion, Version gameVersion,
        bool profileReady, bool profileFailed, bool connected, bool ready,
        IEnumerable<FeatureHealthRecord> health)
    {
        var text = new StringBuilder();
        text.AppendLine("Anomaly early access status");
        text.AppendLine($"Anomaly: {coreVersion}; API: {apiVersion}; game: {gameVersion}");
        text.AppendLine("Profile: " + (profileFailed ? "unavailable (see local log; files preserved)" : profileReady ? "ready" : "setup incomplete"));
        text.AppendLine("Connection: " + (connected ? ready ? "ready" : "connected, preparing" : "disconnected"));
        var unhealthy = health.Where(record => record.Status != FeatureHealthStatus.Healthy)
            .OrderBy(record => record.OwnerId, StringComparer.Ordinal)
            .ThenBy(record => record.FeatureId, StringComparer.Ordinal).ToArray();
        text.AppendLine(unhealthy.Length == 0 ? "Features: healthy" : $"Features needing attention: {unhealthy.Length}");
        foreach (var record in unhealthy.Take(64))
        {
            // Detail, targets and custom remediation can contain endpoints, identities or exception
            // messages. Do not copy them into a shareable report even after best-effort redaction.
            string feature = record.OwnerId == "anomaly" && CoreFeatures.Contains(record.FeatureId)
                ? record.FeatureId : "[module-feature]";
            text.AppendLine($"  {Identifier(record.OwnerId)}:{feature} — {record.Status}; inspect this feature in the local log.");
        }
        if (unhealthy.Length > 64) text.AppendLine("  Additional feature records omitted.");
        text.AppendLine("Log: <game>/MelonLoader/Latest.log");
        text.AppendLine("Crash reports: <MelonLoader UserData>/Anomaly/CrashReports/");
        text.Append("Paths above are relative to this game instance. No report is uploaded.");
        return text.ToString();
    }

    private static string Identifier(string value) => value != null && value.Length <= 128 &&
        Regex.IsMatch(value, @"\A[a-z][a-z0-9_.-]*\z", RegexOptions.CultureInvariant)
            ? value : "[invalid-id]";
}
