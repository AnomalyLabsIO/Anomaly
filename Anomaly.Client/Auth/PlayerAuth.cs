using Newtonsoft.Json;

namespace Anomaly.Client.Auth;

public struct PlayerAuth
{
    [JsonProperty("identity")]
    public string Identity { get; set; }

    [JsonProperty("username")]
    public string Username { get; set; }

    public void SetIdentity(byte[] identity)
    {
        Identity = Convert.ToBase64String(identity);
    }
    public byte[] GetIdentity() => Convert.FromBase64String(Identity);
}
