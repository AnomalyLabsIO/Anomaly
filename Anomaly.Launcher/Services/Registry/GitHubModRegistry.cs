namespace Anomaly.Launcher.Services.Registry;

/// <summary>
/// Placeholder for the planned online mod catalog backed by a GitHub-hosted JSON feed.
/// Not wired into the UI yet — the concrete catalog schema and fetch logic will be
/// designed once the mod-authoring side of v1.x ships.
///
/// When implementing:
///   1. Replace the stub with an <c>HttpClient</c> fetch against <see cref="FeedUrl"/>.
///   2. Deserialize into <see cref="ModRegistryEntry"/> (or extend the record first).
///   3. Cache responses so re-opening the Launcher doesn't always hit the network.
/// </summary>
public sealed class GitHubModRegistry : IModRegistry
{
    public const string DefaultFeedUrl =
        "https://raw.githubusercontent.com/AnomalyLabs/anomaly-mod-index/main/mods.json";

    public string FeedUrl { get; }
    public string Name => $"GitHub ({FeedUrl})";
    public bool IsOnline => true;

    public GitHubModRegistry(string feedUrl = DefaultFeedUrl)
    {
        FeedUrl = feedUrl;
    }

    public Task<IReadOnlyList<ModRegistryEntry>> ListAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "GitHubModRegistry is scaffolded but not wired up. See class docstring for the implementation steps.");
    }
}
