namespace Anomaly.Client.Events.Args;

public class ClientReadyEventArgs : EventArgs
{
    public ClientReadyEventArgs(string sceneName)
    {
        SceneName = sceneName;
    }

    public string SceneName { get; }
}
