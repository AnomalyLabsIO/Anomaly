namespace Anomaly.Client.Api.Events.Args;

/// <summary>The server-disclosed enrollment identifier changed or was cleared.</summary>
public class EnrollmentChangedEventArgs : EventArgs
{
    public EnrollmentChangedEventArgs(string previous, string current)
    {
        Previous = previous;
        Current = current;
    }

    public string Previous { get; }
    public string Current { get; }
}
