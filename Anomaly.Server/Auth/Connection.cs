using Anomaly.Shared.Auth;

namespace Anomaly.Server.Auth;

public class Connection
{
    public ServerAuthSession ServerEnv { get; set; }
    public ClientHandshake Handshake {  get; set; }
}
