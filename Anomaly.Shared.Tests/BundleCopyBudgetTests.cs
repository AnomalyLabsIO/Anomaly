using System.Collections.Generic;
using System.Linq;
using Anomaly.Client.Assets;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class BundleCopyBudgetTests
{
    [Fact]
    public void Observed_personal_skin_and_maximum_offer_conflict_is_known_without_acquiring_bundle_bytes()
    {
        var local = new[] { Copy("personal:skins", 3309850) };
        var remote = RemoteCopies(267679241);
        var error = Assert.Throws<BundleCopyBudgetException>(() => BundleCopyBudget.Validate(local, remote));
        Assert.Contains("256 MiB", error.Message);
        Assert.Contains("Local packs retain", error.Message);
    }

    [Theory]
    [InlineData(267679241L)]
    [InlineData(256L * 1024 * 1024)]
    public void Maximum_server_bundles_fit_without_local_pins(long bytes)
    {
        BundleCopyBudget.Validate(Array.Empty<KeyValuePair<string, long>>(), RemoteCopies(bytes));
    }

    [Fact]
    public void Reusing_an_exact_copy_counts_once_and_another_index_is_a_distinct_copy()
    {
        const long half = BundleCopyBudget.MaximumBytes / 2;
        var local = new[] { Copy("validator:profile:bundle-a:index-a", half) };
        BundleCopyBudget.Validate(local, new[] {
            Copy("validator:profile:bundle-a:index-a", half), Copy("validator:profile:bundle-b:index-b", half) });
        Assert.Throws<BundleCopyBudgetException>(() => BundleCopyBudget.Validate(local, new[] {
            Copy("validator:profile:bundle-a:index-other", half), Copy("validator:profile:bundle-b:index-b", half) }));
    }

    [Fact]
    public void Exactly_128_local_copies_are_allowed_and_reused_keys_do_not_consume_a_new_slot()
    {
        var local = Enumerable.Range(0, 128).Select(index => Copy("local:" + index, 1)).ToArray();
        BundleCopyBudget.Validate(local, Array.Empty<KeyValuePair<string, long>>());
        BundleCopyBudget.Validate(local, new[] { local[0] });
        var error = Assert.Throws<BundleCopyBudgetException>(() => BundleCopyBudget.Validate(local, new[] { Copy("remote:new", 1) }));
        Assert.Contains("128", error.Message);
    }

    private static KeyValuePair<string, long>[] RemoteCopies(long bytes) => Enumerable.Range(0, 8)
        .Select(index => Copy("remote:" + index, bytes / 8 + (index == 0 ? bytes % 8 : 0))).ToArray();

    private static KeyValuePair<string, long> Copy(string key, long bytes) => new(key, bytes);
}
