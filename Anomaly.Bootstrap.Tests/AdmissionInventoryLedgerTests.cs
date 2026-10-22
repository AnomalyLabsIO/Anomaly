using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class AdmissionInventoryLedgerTests
{
    [Fact]
    public void SnapshotsAreDetachedAndRetainEveryObservedOrigin()
    {
        var store = new AdmissionInventoryStore();
        var alpha = new object();
        store.Observe(alpha, "alpha", "1.0");
        var first = store.Capture();
        store.Observe(new object(), "beta", "2.0");
        var second = store.Capture();

        Assert.Single(first.Entries);
        Assert.Equal(new[] { "alpha", "beta" }, second.Entries.Select(entry => entry.Id));
        Assert.True(second.Generation > first.Generation);
        Assert.Throws<NotSupportedException>(() => ((IList<AdmissionInventoryEntry>)first.Entries).Clear());
        store.Observe(alpha, "alpha", "1.0");
        Assert.Equal(second.Generation, store.Capture().Generation);
    }

    [Fact]
    public void OnlyAReservationThatNeverLoadedCanBeRemoved()
    {
        var store = new AdmissionInventoryStore();
        var observed = new object();
        var unloadedReservation = new object();
        store.Observe(observed, "loaded.failed-constructor", "1");
        store.Observe(unloadedReservation, "not.loaded", "1");
        long reservedGeneration = store.Generation;
        store.CancelUnloadedReservation(unloadedReservation);

        Assert.Equal("loaded.failed-constructor", Assert.Single(store.Capture().Entries).Id);
        Assert.True(store.Generation > reservedGeneration);
    }

    [Theory]
    [InlineData("first", "first")]
    [InlineData("First", "FIRST")]
    public void DistinctOriginsCannotHideBehindADuplicateId(string first, string second)
    {
        var store = new AdmissionInventoryStore();
        store.Observe(new object(), first, "1");
        Assert.Throws<InvalidOperationException>(() => store.Observe(new object(), second, "2"));
        Assert.False(store.IsValid);
        Assert.Throws<InvalidOperationException>(() => store.Capture());
    }

    [Fact]
    public void AConflictingOriginalDeclarationCannotReplaceTheObservedIdentity()
    {
        var store = new AdmissionInventoryStore();
        var origin = new object();
        store.Observe(origin, "original", "1");
        Assert.Throws<InvalidOperationException>(() => store.Observe(origin, "replacement", "2"));
        Assert.True(store.TryGetEntry(origin, out var entry));
        Assert.Equal("original", entry.Id);
        Assert.Equal("1", entry.Version);
        Assert.False(store.IsValid);
    }

    [Theory]
    [InlineData("", "1")]
    [InlineData("bad\nname", "1")]
    [InlineData("id", "")]
    public void UnrepresentableMetadataNeverProducesACleanSnapshot(string id, string version)
    {
        var store = new AdmissionInventoryStore();
        Assert.Throws<InvalidOperationException>(() => store.Observe(new object(), id, version));
        Assert.False(store.IsValid);
        Assert.Throws<InvalidOperationException>(() => store.Capture());
    }

    [Fact]
    public void InvalidUtf16CannotEnterTheLedger()
    {
        var store = new AdmissionInventoryStore();
        Assert.Throws<InvalidOperationException>(() => store.Observe(new object(), "id", new string((char)0xd800, 1)));
        Assert.False(store.IsValid);
    }

    [Fact]
    public void CustomResolverInstancesWithOneWrapperTypeKeepSeparateIdentities()
    {
        var origins = new AdmissionOriginIndex();
        var first = new object();
        var second = new object();
        object firstOrigin = origins.Resolve(typeof(object), first, out bool firstDeclared);
        object secondOrigin = origins.Resolve(typeof(object), second, out bool secondDeclared);
        Assert.False(firstDeclared);
        Assert.False(secondDeclared);
        Assert.NotSame(firstOrigin, secondOrigin);
        Assert.Same(firstOrigin, origins.Resolve(typeof(object), first, out _));

        var store = new AdmissionInventoryStore();
        Assert.True(store.ObserveRuntimeIdentity(firstOrigin, "first.wrapper", "1"));
        Assert.True(store.ObserveRuntimeIdentity(secondOrigin, "second.wrapper", "2"));
        Assert.Equal(new[] { "first.wrapper", "second.wrapper" }, store.Capture().Entries.Select(entry => entry.Id));
    }

    [Fact]
    public void EachDistinctRuntimeMetadataChangeInvalidatesTheFrozenGeneration()
    {
        var store = new AdmissionInventoryStore();
        var origin = new object();
        store.Observe(origin, "original", "1");
        long clean = store.Generation;
        Assert.True(store.ObserveRuntimeIdentity(origin, "original", "1"));
        Assert.Equal(clean, store.Generation);
        Assert.False(store.ObserveRuntimeIdentity(origin, "hidden", "2"));
        long first = store.Generation;
        Assert.True(first > clean);
        Assert.False(store.ObserveRuntimeIdentity(origin, "hidden", "2"));
        Assert.Equal(first, store.Generation);
        Assert.False(store.ObserveRuntimeIdentity(origin, "hidden-again", "3"));
        long second = store.Generation;
        Assert.True(second > first);
        Assert.True(store.ObserveRuntimeIdentity(origin, "original", "1"));
        Assert.True(store.Generation > second);
        Assert.True(store.IsValid);
        Assert.Equal("original", Assert.Single(store.Capture().Entries).Id);
    }

    [Fact]
    public void DistinctRegistryDivergenceAndRestorationInvalidateOncePerState()
    {
        var store = new AdmissionInventoryStore();
        var first = new object();
        var second = new object();
        var view = new Dictionary<object, AdmissionRegistryObservation>
        {
            [first] = new(false, true, "first", "1"),
        };
        store.ObserveDivergentRegistry(view);
        long oneMissing = store.Generation;
        store.ObserveDivergentRegistry(view);
        Assert.Equal(oneMissing, store.Generation);
        view[second] = new(false, true, "second", "1");
        store.ObserveDivergentRegistry(view);
        long twoMissing = store.Generation;
        Assert.True(twoMissing > oneMissing);
        view.Clear();
        store.ObserveDivergentRegistry(view);
        long restored = store.Generation;
        Assert.True(restored > twoMissing);
        store.ObserveDivergentRegistry(view);
        Assert.Equal(restored, store.Generation);
    }

    [Fact]
    public void ConflictingReservationIsFatalEvenIfItsFileHasNotLoaded()
    {
        var store = new AdmissionInventoryStore();
        var reservation = new object();
        store.Observe(new object(), "already.claimed", "1");
        Assert.Throws<InvalidOperationException>(() => store.Observe(reservation, "already.claimed", "1"));
        store.CancelUnloadedReservation(reservation);
        Assert.False(store.IsValid);
        Assert.Throws<InvalidOperationException>(() => store.Capture());
    }
}
