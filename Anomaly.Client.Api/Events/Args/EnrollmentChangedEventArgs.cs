using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>The server-disclosed enrollment identifier changed or was cleared.</summary>
[ScriptType]
public class EnrollmentChangedEventArgs : EventArgs
{
    public EnrollmentChangedEventArgs(string previous, string current)
    {
        Previous = previous;
        Current = current;
    }

    [ScriptVisible]
    public string Previous { get; }
    [ScriptVisible]
    public string Current { get; }
}
