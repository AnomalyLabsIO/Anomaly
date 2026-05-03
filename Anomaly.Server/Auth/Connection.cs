using System;
using Anomaly.Shared.Auth;

namespace Anomaly.Server.Auth;

public class Connection
{
    public ServerAuthSession ServerEnv { get; set; }
    public ClientHandshake Handshake {  get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool PasswordRequired { get; set; }
    public byte[] PasswordSalt { get; set; }
    public int PasswordIterations { get; set; }
    public byte[] PasswordNonce { get; set; }
}
