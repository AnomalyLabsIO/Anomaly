namespace Anomaly.Client.Api.Scripting;

/// <summary>Reports the qualification gate for server-supplied client scripts.</summary>
public static class ScriptingPolicy
{
    /// <summary>Memory containment remains unqualified by the experimental allocation allowance.</summary>
    public static bool MemoryHardeningQualified => false;

    /// <summary>Whether the isolated worker passed the current process, memory and capability qualification.</summary>
    public static bool RemoteIsolationQualified { get; internal set; }

    /// <summary>Remote script execution requires qualified worker isolation in addition to exact content and execution consent.</summary>
    public static bool RemoteExecutionEnabled => RemoteIsolationQualified;

    private static string _remoteExecutionDisabledReason = "Remote worker isolation qualification has not completed.";

    /// <summary>The actual worker qualification failure or pending state. This is not a configurable permission.</summary>
    public static string RemoteExecutionDisabledReason
    {
        get => RemoteIsolationQualified ? string.Empty : _remoteExecutionDisabledReason;
        internal set => _remoteExecutionDisabledReason = value;
    }
}
