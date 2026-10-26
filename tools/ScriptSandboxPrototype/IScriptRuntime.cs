namespace Anomaly.ScriptSandboxPrototype;

internal interface IScriptRuntime
{
    RuntimeExecution Execute(string source, SandboxedObjectApi api, SandboxLimits limits);
}

internal sealed record RuntimeExecution(string Outcome, string Message, long? VmPeakBytes = null, int Resumes = 0);
