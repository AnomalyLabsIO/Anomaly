using Anomaly.Server.Content;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class ContentPackDirectoryTests
{
    private static readonly string Plugins = Path.Combine(Path.GetTempPath(), "anomaly-path-tests", "LabAPI", "plugins");

    [Theory]
    [InlineData("global", "global")]
    [InlineData("27988", "27988")]
    [InlineData("global-other", "27988")]
    [InlineData("custom", "27988")]
    public void Default_root_follows_loaded_plugin_scope(string installScope, string expectedScope)
    {
        string plugin = Path.Combine(Plugins, installScope, "nested", "Anomaly.Server.dll");
        string result = ContentPackDirectory.Resolve(Plugins, plugin, 27988, new Config().ContentPackDirectory);

        Assert.Equal(Path.Combine(Plugins, expectedScope, "Anomaly", "ContentPacks"), result);
        Assert.True(Path.IsPathRooted(result));
    }

    [Theory]
    [InlineData("global")]
    [InlineData("27988")]
    public void Default_and_relative_override_do_not_follow_working_directory(string scope)
    {
        string plugin = Path.Combine(Plugins, scope, "Anomaly.Server.dll");
        string originalDirectory = Environment.CurrentDirectory;
        string unrelatedDirectory = Path.Combine(Path.GetTempPath(), "anomaly-cwd-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unrelatedDirectory);
        try
        {
            string before = ContentPackDirectory.Resolve(Plugins, plugin, 27988, "ContentPacks");
            Environment.CurrentDirectory = unrelatedDirectory;

            Assert.Equal(before, ContentPackDirectory.Resolve(Plugins, plugin, 27988, "ContentPacks"));
            Assert.Equal(Path.Combine(Plugins, scope, "Anomaly", "owner-packs"),
                ContentPackDirectory.Resolve(Plugins, plugin, 27988, Path.Combine("custom", "..", "owner-packs")));
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(unrelatedDirectory);
        }
    }

    [Theory]
    [InlineData("global")]
    [InlineData("27988")]
    public void Absolute_override_is_retained_in_either_scope(string scope)
    {
        string absolute = Path.Combine(Path.GetTempPath(), "anomaly-owner-content");
        Assert.Equal(absolute, ContentPackDirectory.Resolve(Plugins,
            Path.Combine(Plugins, scope, "Anomaly.Server.dll"), 27988, absolute));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_override_preserves_disabled_disk_pack_behavior(string configured)
    {
        Assert.Null(ContentPackDirectory.Resolve(Plugins, Path.Combine(Plugins, "global", "Anomaly.Server.dll"), 27988, configured));
    }

    [Fact]
    public void Missing_loader_metadata_uses_current_port_instead_of_working_directory()
    {
        Assert.Equal(Path.Combine(Plugins, "27988", "Anomaly", "ContentPacks"),
            ContentPackDirectory.Resolve(Plugins, null, 27988, "ContentPacks"));
    }
}
