namespace Anomaly.Client.Scripting.Remote.Isolation;

internal sealed record WorkerSandboxOptions(
    string DotnetExecutable,
    string WorkerAssembly,
    IReadOnlyList<string> ReadableFiles,
    IReadOnlyList<string> ProtectedDirectories);
