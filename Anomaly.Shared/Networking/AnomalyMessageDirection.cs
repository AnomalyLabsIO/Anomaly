namespace Anomaly.Shared.Networking;

public enum AnomalyMessageDirection : byte
{
    Bidirectional = 0,
    ClientToServer = 1,
    ServerToClient = 2
}
