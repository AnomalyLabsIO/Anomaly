using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
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
        IEnumerable<FeatureHealthRecord> health, Func<string, object[], string> translate = null)
    {
        translate ??= Tr.Get;
        string Text(string key, params object[] args) => translate(key, args);

        var text = new StringBuilder();
        text.AppendLine(Text(TrKeys.Status_Title));
        text.AppendLine(Text(TrKeys.Status_Versions, coreVersion, apiVersion, gameVersion));
        text.AppendLine(Text(TrKeys.Status_Profile, Text(profileFailed ? TrKeys.Status_ProfileUnavailable
            : profileReady ? TrKeys.Status_ProfileReady : TrKeys.Status_ProfileIncomplete)));
        text.AppendLine(Text(TrKeys.Status_Connection, Text(connected
            ? ready ? TrKeys.Status_ConnectionReady : TrKeys.Status_ConnectionPreparing : TrKeys.Status_Disconnected)));
        var unhealthy = health.Where(record => record.Status != FeatureHealthStatus.Healthy)
            .OrderBy(record => record.OwnerId, StringComparer.Ordinal)
            .ThenBy(record => record.FeatureId, StringComparer.Ordinal).ToArray();
        text.AppendLine(unhealthy.Length == 0 ? Text(TrKeys.Status_FeaturesHealthy)
            : Text(TrKeys.Status_FeaturesAttention, unhealthy.Length));
        foreach (var record in unhealthy.Take(64))
        {
            // Detail, targets and custom remediation can contain endpoints, identities or exception
            // messages. Do not copy them into a shareable report even after best-effort redaction.
            string feature = record.OwnerId == "anomaly" && CoreFeatures.Contains(record.FeatureId)
                ? record.FeatureId : "[module-feature]";
            text.AppendLine(Text(TrKeys.Status_FeatureDetail, Identifier(record.OwnerId), feature, record.Status));
        }
        if (unhealthy.Length > 64) text.AppendLine(Text(TrKeys.Status_FeaturesOmitted));
        text.AppendLine(Text(TrKeys.Status_Log, "<game>/MelonLoader/Latest.log"));
        text.AppendLine(Text(TrKeys.Status_CrashReports, "<MelonLoader UserData>/Anomaly/CrashReports/"));
        text.Append(Text(TrKeys.Status_Footer));
        return text.ToString();
    }

    private static string Identifier(string value) => value != null && value.Length <= 128 &&
        Regex.IsMatch(value, @"\A[a-z][a-z0-9_.-]*\z", RegexOptions.CultureInvariant)
            ? value : "[invalid-id]";
}
