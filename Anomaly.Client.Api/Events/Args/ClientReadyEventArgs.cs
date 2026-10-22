using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class ClientReadyEventArgs : EventArgs
{
    public ClientReadyEventArgs(string sceneName)
    {
        SceneName = sceneName;
    }

    [ScriptVisible]
    public string SceneName { get; }
}
