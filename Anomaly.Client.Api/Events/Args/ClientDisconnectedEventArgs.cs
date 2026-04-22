namespace Anomaly.Client.Api.Events.Args;

public class ClientDisconnectedEventArgs : EventArgs
{
    public ClientDisconnectedEventArgs(string serverIp, ushort serverPort)
    {
        ServerIp = serverIp;
        ServerPort = serverPort;
    }

    public string ServerIp { get; }

    public ushort ServerPort { get; }
}
