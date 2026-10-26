namespace Anomaly.ScriptSandboxPrototype;

internal sealed record SandboxLimits
{
    public int MaxSourceBytes { get; init; } = 64 * 1024;
    public int MaxObjects { get; init; } = 16;
    public int MaxCommands { get; init; } = 128;
    public int MaxLabelLength { get; init; } = 128;
    public double MaxCoordinate { get; init; } = 1000;
    public int InstructionSlice { get; init; } = 10000;
    public int MaxResumes { get; init; } = 20;
    public int ExecutionTimeoutMs { get; init; } = 100;
    public long VmMemoryLimitBytes { get; init; } = 4 * 1024 * 1024;
}
