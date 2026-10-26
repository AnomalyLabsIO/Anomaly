using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Scripting;

internal static class RemoteExecutionGate
{
    internal static void EnsureAllowed()
    {
        // Admission additionally requires exact verified source and connection approval.
        // Consent cannot bypass an unqualified memory boundary.
        if (!ScriptingPolicy.RemoteExecutionEnabled)
            throw new NotSupportedException(ScriptingPolicy.RemoteExecutionDisabledReason);
    }
}
