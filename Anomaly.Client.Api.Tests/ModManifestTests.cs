using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Content;
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
    };

    [Fact]
    public void Item_catalog_rejects_unknown_assets()
    {
        var catalog = new ItemCatalog
        {
            ServerFingerprint = new string('f', 64),
            CatalogSha256 = new string('a', 64),
            Assets =
            {
                new PassiveAssetReference
                {
                    Key = "example.asset",
                    Kind = PassiveAssetKind.TexturePng,
                    Sha256 = new string('b', 64),
                    SizeBytes = ItemContentLimits.MaxAssetBytes,
                },
            },
            Items =
            {
                new ItemDefinition
                {
                    Key = "example.item",
                    DisplayName = "Example",
                    BuiltInPrefab = "Coin",
                    AssetKeys = { "example.missing" },
                },
            },
        };

        var errors = ItemCatalogValidator.Validate(catalog);

        Assert.Contains(errors, error => error.Contains("unknown asset", StringComparison.Ordinal));
    }
}
