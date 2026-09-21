namespace Anomaly.Server;

public class Config
{
    /// <summary>
    /// Path to the server's asset directory. Files in this directory
    /// will be transferred to clients on connection. Leave empty to disable.
    /// </summary>
    public string AssetDirectory { get; set; } = "";

    /// <summary>
    /// Requests password-gated admission. Public/remote password authentication is
    /// deliberately unavailable until a vetted OPAQUE implementation supports both
    /// target runtimes, so enabling this rejects remote clients. The only current
    /// compatibility path is the separately enabled loopback-only legacy proof.
    /// </summary>
    public bool RequirePassword { get; set; } = false;

    /// <summary>
    /// Temporary compatibility switch for the legacy verifier-equivalent password
    /// proof. No vetted OPAQUE implementation supports both target runtimes yet, so
    /// this mode is disabled by default and, when enabled, is accepted only from a
    /// loopback address. It must not be exposed to internet clients.
    /// </summary>
    public bool EnableLegacyPasswordAuthOnLoopback { get; set; } = false;

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

    /// <summary>
    /// When true, a client whose custom-content catalog hash does not match the server's
    /// (or that fails to register catalog entries) is disconnected. Default false: the
    /// server instead logs the mismatch and refuses to grant the affected custom content
    /// to that client, leaving the rest of the session intact.
    /// </summary>
}
