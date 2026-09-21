using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Compatibility;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class ModManifestTests
{
    [Fact]
    public void Validator_rejects_reserved_id_and_invalid_hash()
    {
        var manifest = Valid("anomaly.evil", "1.0.0");
        manifest.Sha256 = "not-a-hash";

        var errors = ModManifestValidator.Validate(manifest);

        Assert.Contains(errors, error => error.Contains("reserved", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Sha256", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolver_orders_required_dependencies_deterministically()
    {
        var feature = Valid("example.feature", "2.0.0");
        feature.Dependencies.Add(new ModDependency
        {
            Id = "example.core",
            MinimumVersion = "1.0.0",
            MaximumVersionExclusive = "2.0.0",
        });
        var core = Valid("example.core", "1.4.0");

        var result = ModDependencyResolver.Resolve(new[] { feature, core });

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(new[] { "example.core", "example.feature" }, result.Ordered.Select(x => x.Id));
    }

    [Fact]
    public void Resolver_rejects_cycles()
    {
        var first = Valid("example.first", "1.0.0");
        var second = Valid("example.second", "1.0.0");
        first.Dependencies.Add(new ModDependency { Id = second.Id });
        second.Dependencies.Add(new ModDependency { Id = first.Id });

        var result = ModDependencyResolver.Resolve(new[] { second, first });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("cycle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolver_rejects_conflicts_and_out_of_range_dependencies()
    {
        var core = Valid("example.core", "2.0.0");
        var feature = Valid("example.feature", "1.0.0");
        feature.Conflicts.Add(core.Id);
        feature.Dependencies.Add(new ModDependency
        {
            Id = core.Id,
            MinimumVersion = "1.0.0",
            MaximumVersionExclusive = "2.0.0",
        });

        var result = ModDependencyResolver.Resolve(new[] { feature, core });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("conflicts", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("range", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_rejects_framework_api_outside_declared_range()
    {
        var manifest = Valid("example.future", "1.0.0");
        manifest.MinimumApiVersion = "2.0.0";
        manifest.MaximumApiVersionExclusive = "3.0.0";
        manifest.ApiVersion = "2.0.0";

        var errors = ModManifestValidator.Validate(manifest);

        Assert.Contains(errors, error => error.Contains("Framework API", StringComparison.Ordinal));
    }

    private static ModManifest Valid(string id, string version) => new()
    {
        Id = id,
        Name = id,
        Version = version,
        ApiVersion = ApiContract.CurrentVersion,
        Sha256 = new string('a', 64),
        SupportedGameVersions = { "14.2.7^" },
    };

    [Theory]
    [InlineData(14, 2, 7, true)]
    [InlineData(14, 2, 99, true)]
    [InlineData(14, 2, 6, false)]
    [InlineData(14, 3, 0, false)]
    public void Caret_game_version_is_bounded_to_selected_patch_line(
        int major, int minor, int revision, bool supported)
    {
        Assert.Equal(supported, GameVersionCompatibility.IsSupported(
            new[] { "14.2.7^" }, major, minor, revision));
    }

    [Theory]
    [InlineData("14.2147483647.7^")]
    [InlineData("14.2.2147483647")]
    [InlineData("+14.2.7^")]
    [InlineData("14.+2.7^")]
    [InlineData("14.2.+7^")]
    public void Game_version_range_overflow_or_noncanonical_numeric_syntax_is_rejected(string expression)
    {
        Assert.False(GameVersionCompatibility.TryParse(expression, out _, out _));
    }

}
