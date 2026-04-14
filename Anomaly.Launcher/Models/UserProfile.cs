using Newtonsoft.Json;

namespace Anomaly.Launcher.Models;

public class UserProfile
{
    [JsonProperty("identity")]
    public string? Identity { get; set; }

    [JsonProperty("username")]
    public string? Username { get; set; }
}
