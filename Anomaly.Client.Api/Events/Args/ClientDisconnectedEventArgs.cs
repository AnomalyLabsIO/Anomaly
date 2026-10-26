using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class ClientDisconnectedEventArgs : EventArgs
{
    public ClientDisconnectedEventArgs(string serverIp, ushort serverPort)
    {
        ServerIp = serverIp;
        ServerPort = serverPort;
    }

    [ScriptVisible]
    public string ServerIp { get; }

    [ScriptVisible]
    public ushort ServerPort { get; }
}
