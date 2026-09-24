using System.Collections.Generic;

namespace Anomaly.Server;

public class Config
{
    /// <summary>
    /// Directory of completed offline-authored .index.json and .bundle pairs.
    /// Pairs are indexed on a worker and offered after authentication. Leave empty to disable.
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

    /// <summary>Rejects reported client mods other than Anomaly and mods required by active server plugins.</summary>
    public bool BlockAnyAdditionalClientMods { get; set; } = false;

    /// <summary>Mod IDs that are denied even if a server plugin requires them.</summary>
    public List<string> BlockedClientModIds { get; set; } = new List<string>();

}
