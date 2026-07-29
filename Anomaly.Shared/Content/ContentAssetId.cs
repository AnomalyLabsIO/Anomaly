using Anomaly.Shared.Networking;

namespace Anomaly.Shared.Content
{
    /// <summary>
    /// Deterministic Mirror assetId for a custom pickup prefab, derived purely from the content
    /// key so the server and every client independently arrive at the same value. This is what
    /// lets a server-spawned custom pickup resolve to the matching client-registered prefab.
    /// </summary>
    public static class ContentAssetId
    {
        public static uint For(string key) =>
            AnomalyMessageRegistry.ComputeHash("anomaly:content:asset:" + (key ?? ""));
    }
}
