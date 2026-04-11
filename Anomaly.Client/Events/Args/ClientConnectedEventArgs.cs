namespace Anomaly.Client.Events.Args;

public class ClientConnectedEventArgs : EventArgs
{
    public ClientConnectedEventArgs(string serverIp, ushort serverPort)
    {
        ServerIp = serverIp;
        ServerPort = serverPort;
    }

    public string ServerIp { get; }

    public ushort ServerPort { get; }
}
