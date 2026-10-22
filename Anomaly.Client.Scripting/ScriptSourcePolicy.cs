using System.Text;

namespace Anomaly.Client.Scripting;

internal static class ScriptSourcePolicy
{
    internal static string? Reject(string? source, ScriptExecutionOptions options)
    {
        if (source == null || source.Length > options.MaxSourceBytes || Encoding.UTF8.GetByteCount(source) > options.MaxSourceBytes)
            return "Script source exceeds its byte limit or is missing.";
        if (source.IndexOf('\0') >= 0 || source.IndexOf('\x1b') >= 0 || source.Contains("MoonSharp_dump_b64::", StringComparison.Ordinal))
            return "Only source text is accepted; bytecode and dumped chunks are disabled.";
        return null;
    }
}
