namespace Anomaly.Client.Scripting;

internal sealed record ScriptExecutionOptions
{
    public long InstructionLimit { get; init; } = 100_000;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(100);
    public int MaxCallDepth { get; init; } = 64;
    public int InstructionSlice { get; init; } = 2_000;
    public int MaxSourceBytes { get; init; } = 64 * 1024;
    public int MaxResultStringBytes { get; init; } = 4096;
    public long AllocationLimitBytes { get; init; } = 16 * 1024 * 1024;

    internal void Validate()
    {
        if (InstructionLimit <= 0 || Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromSeconds(5) ||
            MaxCallDepth < 1 || MaxCallDepth > 256 || InstructionSlice < 1 ||
            MaxSourceBytes < 1 || MaxSourceBytes > 1024 * 1024 || MaxResultStringBytes < 1 || MaxResultStringBytes > 64 * 1024 ||
            AllocationLimitBytes < 64 * 1024 || AllocationLimitBytes > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(ScriptExecutionOptions), "Invalid development script execution limits.");
    }
}
