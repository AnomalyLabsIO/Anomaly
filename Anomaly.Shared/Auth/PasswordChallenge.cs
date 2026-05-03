namespace Anomaly.Shared.Auth;

public class PasswordChallenge
{
    public bool Required { get; set; }
    public byte[] Salt { get; set; }
    public int Iterations { get; set; }
    public byte[] Nonce { get; set; }

    public static PasswordChallenge NotRequired()
    {
        return new PasswordChallenge
        {
            Required = false,
            Salt = new byte[0],
            Iterations = 0,
            Nonce = new byte[0],
        };
    }
}
