namespace Anomaly.Server;

public class Config
{
    /// <summary>
    /// Path to the server's asset directory. Files in this directory
    /// will be transferred to clients on connection. Leave empty to disable.
    /// </summary>
    public string AssetDirectory { get; set; } = "";

    /// <summary>
    /// Requires clients to prove knowledge of the server password before Mirror
    /// accepts the connection. Set the password with LocalAdmin: password &lt;value&gt;.
    /// </summary>
    public bool RequirePassword { get; set; } = false;

    /// <summary>
    /// PBKDF2 iterations used when saving a new password with the LocalAdmin
    /// password command. Existing verifier files keep their stored iteration count.
    /// </summary>
    public int PasswordKdfIterations { get; set; } = 120000;

    /// <summary>
    /// Number of bad password proofs allowed per IP before temporary blocking.
    /// Set to 0 to disable password failure blocking.
    /// </summary>
    public int PasswordMaxFailuresPerIp { get; set; } = 5;

    /// <summary>
    /// Duration of the temporary password failure block in seconds.
    /// </summary>
    public int PasswordBlockSeconds { get; set; } = 300;
}
