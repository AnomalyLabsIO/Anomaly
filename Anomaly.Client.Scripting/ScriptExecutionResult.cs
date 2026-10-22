namespace Anomaly.Client.Scripting;

internal enum ScriptExecutionStatus
{
    Completed,
    SourceRejected,
    ScriptError,
    CpuLimit,
    Deadline,
    DepthLimit,
    Cancelled,
    InvalidResult,
    Retired
}

internal sealed record ScriptExecutionResult(ScriptExecutionStatus Status, string Message, object? Value = null)
{
    public bool Succeeded => Status == ScriptExecutionStatus.Completed;
}
