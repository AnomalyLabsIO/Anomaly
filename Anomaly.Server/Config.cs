using System.Collections.Generic;

namespace Anomaly.Server;

public class Config
{
    /// <summary>
    /// Root of descriptor packs at namespace/pack/pack.json. Relative paths are
    /// anchored to LabAPI plugins/global/Anomaly or plugins/&lt;port&gt;/Anomaly,
    /// matching the loaded plugin's scope, not the process working directory.
    /// Absolute paths are retained. Captured on a worker for authenticated offers.
    /// Empty disables owner packs.
    /// </summary>
    public string ContentPackDirectory { get; set; } = "ContentPacks";

    /// <summary>Per-client accounted upload allowance in bytes/second before gameplay reserve (64 KiB–16 MiB/s).</summary>
    public int BulkClientBytesPerSecond { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// Aggregate accounted upload allowance in bytes/second before gameplay reserve (64 KiB–64 MiB/s).
    /// Set at or below the available server uplink; the default is not automatic link detection.
    /// Bulk accounting includes a 25 percent retransmission/framing margin plus 256 bytes per chunk.
    /// </summary>
    public int BulkUploadBytesPerSecond { get; set; } = 16 * 1024 * 1024;

    /// <summary>Percentage of both upload allowances reserved for gameplay/control (10–90). Requires WAN qualification.</summary>
    public int BulkGameplayHeadroomPercent { get; set; } = 20;

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

    /// <summary>
    /// Accepts authenticated clients reporting external authentication-path modifications or monitoring gaps.
    /// This does not bypass required, blocked or additional-mod rules. Client claims are not attestation.
    /// </summary>
    public bool AllowRiskyClientModifications { get; set; } = false;

}
