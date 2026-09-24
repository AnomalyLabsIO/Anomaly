using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Anomaly.Shared.Auth;

public sealed class ModAdmissionIssue
{
    public string Code { get; }
    public string Id { get; }
    public string Required { get; }
    public string Present { get; }

    public ModAdmissionIssue(string code, string id, string required, string present)
    {
        if (!ModAdmissionResponseCodec.IsKnownCode(code))
            throw new ArgumentException("Unknown mod admission issue code.", nameof(code));
        Code = code;
        Id = ClientModInventory.ValidateText(id, ClientModInventory.MaxIdBytes, nameof(id));
        Required = required == null ? null : ClientModInventory.ValidateText(required, 128, nameof(required));
        Present = present == null ? null : ClientModInventory.ValidateText(present, ClientModInventory.MaxVersionBytes, nameof(present));
    }
}

public sealed class ModAdmissionResponse
{
    public const string Discriminator = "anomaly.mod_admission";
    public const int SchemaVersion = 1;

    public string Kind => Discriminator;
    public int Version => SchemaVersion;
    public IReadOnlyList<ModAdmissionIssue> Issues { get; }
    public int OmittedCount { get; }

    internal ModAdmissionResponse(IReadOnlyList<ModAdmissionIssue> issues, int omittedCount)
    {
        Issues = issues;
        OmittedCount = omittedCount;
    }
}

/// <summary>Compact custom rejection JSON, bounded for LiteNetLib's minimum rejection packet.</summary>
public static class ModAdmissionResponseCodec
{
    public const int MaxUtf8Bytes = 460;
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static string EncodeBounded(IReadOnlyList<ModAdmissionIssue> issues)
    {
        if (issues == null)
            throw new ArgumentNullException(nameof(issues));
        if (issues.Any(issue => issue == null))
            throw new ArgumentException("Issues cannot contain null entries.", nameof(issues));

        var sorted = issues
            .OrderBy(issue => issue.Id, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Required, StringComparer.Ordinal)
            .ThenBy(issue => issue.Present, StringComparer.Ordinal)
            .ToArray();

        var included = new List<ModAdmissionIssue>();
        var best = Serialize(included, sorted.Length);
        if (StrictUtf8.GetByteCount(best) > MaxUtf8Bytes)
            throw new InvalidOperationException("The empty mod admission response exceeds the packet bound.");

        foreach (var issue in sorted)
        {
            included.Add(issue);
            var candidate = Serialize(included, sorted.Length - included.Count);
            if (StrictUtf8.GetByteCount(candidate) > MaxUtf8Bytes)
                included.RemoveAt(included.Count - 1);
            else
                best = candidate;
        }
        return best;
    }

    public static bool TryDecode(string json, out ModAdmissionResponse response)
    {
        response = null;
        if (string.IsNullOrEmpty(json))
            return false;

        try
        {
            var bytes = StrictUtf8.GetBytes(json);
            if (bytes.Length > MaxUtf8Bytes)
                return false;
            using (var stream = new MemoryStream(bytes, false))
            {
                var serializer = new DataContractJsonSerializer(typeof(ResponseWire));
                var wire = serializer.ReadObject(stream) as ResponseWire;
                if (wire == null || stream.Position != stream.Length
                    || wire.Kind != ModAdmissionResponse.Discriminator
                    || wire.Version != ModAdmissionResponse.SchemaVersion
                    || wire.Issues == null || wire.Issues.Length > ClientModInventory.MaxEntries
                    || wire.OmittedCount < 0)
                    return false;

                var issues = new ModAdmissionIssue[wire.Issues.Length];
                for (var i = 0; i < issues.Length; i++)
                {
                    var issue = wire.Issues[i];
                    if (issue == null)
                        return false;
                    issues[i] = new ModAdmissionIssue(issue.Code, issue.Id, issue.Required, issue.Present);
                }
                if (issues.Length == 0 && wire.OmittedCount == 0)
                    return false;
                response = new ModAdmissionResponse(Array.AsReadOnly(issues), wire.OmittedCount);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsKnownCode(string code)
    {
        return code == "missing" || code == "wrong_version" || code == "blocked" || code == "unexpected";
    }

    private static string Serialize(IReadOnlyList<ModAdmissionIssue> included, int omittedCount)
    {
        var wire = new ResponseWire
        {
            Kind = ModAdmissionResponse.Discriminator,
            Version = ModAdmissionResponse.SchemaVersion,
            Issues = included.Select(issue => new IssueWire
            {
                Code = issue.Code,
                Id = issue.Id,
                Required = issue.Required,
                Present = issue.Present,
            }).ToArray(),
            OmittedCount = omittedCount,
        };

        using (var stream = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(ResponseWire)).WriteObject(stream, wire);
            return StrictUtf8.GetString(stream.ToArray());
        }
    }

    [DataContract]
    private sealed class ResponseWire
    {
        [DataMember(Name = "kind", Order = 0)] public string Kind;
        [DataMember(Name = "version", Order = 1)] public int Version;
        [DataMember(Name = "issues", Order = 2)] public IssueWire[] Issues;
        [DataMember(Name = "omittedCount", Order = 3)] public int OmittedCount;
    }

    [DataContract]
    private sealed class IssueWire
    {
        [DataMember(Name = "code", Order = 0)] public string Code;
        [DataMember(Name = "id", Order = 1)] public string Id;
        [DataMember(Name = "required", Order = 2, EmitDefaultValue = false)] public string Required;
        [DataMember(Name = "present", Order = 3, EmitDefaultValue = false)] public string Present;
    }
}
