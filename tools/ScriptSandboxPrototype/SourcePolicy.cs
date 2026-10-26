using System.Text;

namespace Anomaly.ScriptSandboxPrototype;

internal static class SourcePolicy
{
    public static string? Reject(string source, SandboxLimits limits)
    {
        if (Encoding.UTF8.GetByteCount(source) > limits.MaxSourceBytes)
            return "source exceeds the package byte limit";
        // MoonSharp LoadString also accepts its Base64 binary-dump text format.
        // Explicitly forbid binary input instead of treating a string as proof of source.
        if (source.IndexOf('\0') >= 0 || source.IndexOf('\x1b') >= 0 || source.Contains("MoonSharp_dump_b64::", StringComparison.Ordinal))
            return "only source text is accepted; bytecode/dump input is disabled";
        return null;
    }
}
