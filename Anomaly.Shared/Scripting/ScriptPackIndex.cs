using System.Collections.Generic;

namespace Anomaly.Shared.Scripting;

/// <summary>Generated metadata binding a source inventory and message contracts.</summary>
public sealed class ScriptPackIndex
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion = CurrentSchemaVersion;
    public string PackId = string.Empty;
    public int ApiVersion = ScriptPackLimits.ApiVersion;
    public List<ScriptPackSource> Sources = new List<ScriptPackSource>();
    public List<ScriptMessageSchema> Messages = new List<ScriptMessageSchema>();
}

public sealed class ScriptPackSource
{
    public string Address = string.Empty;
    public string SourceSha256 = string.Empty;
    public int SourceBytes;
}
