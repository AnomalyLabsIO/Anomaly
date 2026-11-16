using Anomaly.Client.Assets;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class BundleDecodedBudgetTests
{
    [Fact]
    public void Small_encoded_offer_is_rejected_when_combined_decoded_residency_cannot_fit()
    {
        var local = new[] { Demand("skin", "skin:index", 41697280) };
        var remote = new[] { Demand("remote", "remote:index", 267745256) };
        var error = Assert.Throws<BundleCopyBudgetException>(() => BundleCopyBudget.ValidateDecoded(local, remote));
        Assert.Contains(309442536L.ToString("N0"), error.Message);
        Assert.Contains("decoded asset capacity", error.Message);
    }

    [Fact]
    public void Native_bundle_reuse_counts_once_but_an_inconsistent_index_is_not_accepted()
    {
        var copy = Demand("same-bundle", "exact-index", BundleCopyBudget.MaximumBytes);
        BundleCopyBudget.ValidateDecoded(new[] { copy }, new[] { copy });
        Assert.Throws<InvalidDataException>(() => BundleCopyBudget.ValidateDecoded(new[] { copy },
            new[] { Demand("same-bundle", "different-index", copy.Bytes) }));
        Assert.Throws<InvalidDataException>(() => BundleCopyBudget.ValidateDecoded(new[] { copy },
            new[] { Demand("same-bundle", "exact-index", copy.Bytes - 1) }));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(268435457L)]
    public void Missing_or_unbounded_declarations_cannot_enter_capacity_planning(long bytes) =>
        Assert.Throws<InvalidDataException>(() => BundleCopyBudget.ValidateDecoded(Array.Empty<BundleCopyBudget.DecodedDemand>(),
            new[] { Demand("bundle", "index", bytes) }));

    [Theory]
    [InlineData(358375L)]
    [InlineData(358377L)]
    public void Both_underdeclared_and_overdeclared_resource_summaries_fail_independent_validation(long declared) =>
        Assert.Throws<InvalidDataException>(() => BundleCopyBudget.ValidateMeasurement(declared, 358376));

    [Fact]
    public void Exact_independent_measurement_is_accepted() => BundleCopyBudget.ValidateMeasurement(358376, 358376);

    private static BundleCopyBudget.DecodedDemand Demand(string hash, string key, long bytes) => new(hash, key, bytes);
}
