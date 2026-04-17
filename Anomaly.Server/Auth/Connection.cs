using System;
using Anomaly.Shared.Auth;

namespace Anomaly.Server.Auth;

public class Connection
{
    public ServerAuthSession ServerEnv { get; set; }
    public ClientHandshake Handshake {  get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
