namespace Anomaly.Client.Api.Scripting;

/// <summary>Reports the qualification gate for server-supplied client scripts.</summary>
public static class ScriptingPolicy
{
    /// <summary>Memory containment remains unqualified by the experimental allocation allowance.</summary>
    public static bool MemoryHardeningQualified => false;

    /// <summary>Remote script execution requires a qualified memory boundary in addition to content consent.</summary>
    public static bool RemoteExecutionEnabled => MemoryHardeningQualified;

    /// <summary>The reason remote execution is unavailable. This is not a configurable permission.</summary>
    public static string RemoteExecutionDisabledReason => MemoryHardeningQualified ? string.Empty :
        "Remote scripts are disabled until memory containment is qualified. Accounted VM allocations do not contain arbitrary CLR or native host allocations.";
}
