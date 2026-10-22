namespace Anomaly.Client.Api.Scripting;

/// <summary>Reports the qualification gate for server-supplied client scripts.</summary>
public static class ScriptingPolicy
{
    /// <summary>Memory containment is not qualified by the CPU-hardening implementation.</summary>
    public static bool MemoryHardeningQualified => false;

    /// <summary>Server-supplied execution remains unavailable until memory hardening is qualified.</summary>
    public static bool RemoteExecutionEnabled => false;

    /// <summary>The reason remote execution is unavailable. This is not a configurable permission.</summary>
    public static string RemoteExecutionDisabledReason =>
        "Remote script execution is disabled: per-script memory containment has not been qualified.";
}
