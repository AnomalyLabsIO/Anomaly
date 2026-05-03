using Anomaly.Shared.Auth;

namespace Anomaly.Client.Auth;

public class ServerConnection
{
    public ClientAuthSession Session { get; set; }
    public ServerHandshake Handshake { get; set; }
    public string ServerIdentifier { get; set; }
    public PasswordChallenge PasswordChallenge { get; set; }
    public byte[] PasswordProof { get; set; }
}
