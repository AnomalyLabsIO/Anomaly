namespace Anomaly.Server;

public class Config
{
    /// <summary>
    /// Path to the server's asset directory. Files in this directory
    /// will be transferred to clients on connection. Leave empty to disable.
    /// </summary>
    public string AssetDirectory { get; set; } = "";
}
