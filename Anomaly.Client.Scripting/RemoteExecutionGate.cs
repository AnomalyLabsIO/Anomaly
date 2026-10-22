using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Scripting;

internal static class RemoteExecutionGate
{
    internal static void EnsureAllowed()
    {
        // Deliberately has no config/environment/consent override. CPU qualification
        // does not authorize remote execution while memory containment is pending.
        if (!ScriptingPolicy.MemoryHardeningQualified || !ScriptingPolicy.RemoteExecutionEnabled)
            throw new NotSupportedException(ScriptingPolicy.RemoteExecutionDisabledReason);
    }
}
