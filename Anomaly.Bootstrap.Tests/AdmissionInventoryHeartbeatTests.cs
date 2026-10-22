using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class AdmissionInventoryHeartbeatTests
{
    [Fact]
    public void Partial_registry_view_cannot_replace_complete_divergence_state()
    {
        var store = new AdmissionInventoryStore();
        var first = new object();
        var second = new object();
        store.Observe(first, "first", "1");
        store.Observe(second, "second", "1");
        store.ObserveDivergentRegistry(new Dictionary<object, AdmissionRegistryObservation>
        { [second] = new(false, true, "second", "1") });
        long completedGeneration = store.Generation;
        var registered = Registry((first, "first"), (second, "second"));
        var cycle = new AdmissionRegistryReconciliation(7, registered, new[] { second });

        Assert.True(cycle.TryTake(out object item));
        Assert.False(cycle.TryPublish(7, store)); // Taken is not yet observed.
        cycle.Observe(item, true, registered[item]);
        Assert.False(cycle.TryPublish(7, store));
        Assert.Equal(completedGeneration, store.Generation);
        while (cycle.TryTake(out item)) cycle.Observe(item, true, registered[item]);
        Assert.True(cycle.TryPublish(7, store));
        Assert.True(store.Generation > completedGeneration);
        Assert.Equal(new[] { "first", "second" }, store.Capture().Entries.Select(entry => entry.Id));
    }

    [Fact]
    public void Lifecycle_revision_change_rejects_even_a_fully_processed_old_view()
    {
        var store = new AdmissionInventoryStore();
        var mod = new object();
        store.Observe(mod, "mod", "1");
        long generation = store.Generation;
        var registry = Registry((mod, "mod"));
        var old = new AdmissionRegistryReconciliation(2, registry, Array.Empty<object>());
        Assert.True(old.TryTake(out object item));
        Assert.True(old.Observe(item, false, registry[item]));
        Assert.False(old.TryPublish(3, store));
        Assert.Equal(generation, store.Generation);

        var current = new AdmissionRegistryReconciliation(3, registry, new[] { mod });
        Assert.True(current.TryTake(out item));
        Assert.False(current.Observe(item, true, registry[item]));
        Assert.True(current.TryPublish(3, store));
        Assert.Equal(generation, store.Generation);
    }

    [Fact]
    public void Individual_metadata_discovery_invalidates_generation_before_full_publication()
    {
        var store = new AdmissionInventoryStore();
        var first = new object();
        var second = new object();
        store.Observe(first, "first", "1");
        store.Observe(second, "second", "1");
        long frozen = store.Generation;
        var registry = Registry((first, "first"), (second, "second"));
        var cycle = new AdmissionRegistryReconciliation(1, registry, new[] { first, second });
        Assert.True(cycle.TryTake(out object item));
        var current = new AdmissionInventoryEntry(registry[item].Id, "changed");
        Assert.False(store.ObserveRuntimeIdentity(item, current.Id, current.Version));
        Assert.True(cycle.Observe(item, true, current));
        Assert.False(cycle.TryPublish(1, store));
        Assert.True(store.Generation > frozen);
        Assert.All(store.Capture().Entries, entry => Assert.Equal("1", entry.Version));
    }

    [Fact]
    public void Registry_snapshot_is_detached_and_processes_each_union_member_once()
    {
        var first = new object();
        var second = new object();
        var later = new object();
        var registry = Registry((first, "first"));
        var live = new List<object> { first, first, second };
        var cycle = new AdmissionRegistryReconciliation(4, registry, live);
        registry.Clear();
        live.Clear();
        live.Add(later);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        while (cycle.TryTake(out object item))
        {
            Assert.True(seen.Add(item));
            var entry = new AdmissionInventoryEntry(ReferenceEquals(item, first) ? "first" : "second", "1");
            Assert.Equal(ReferenceEquals(item, second), cycle.Observe(item, true, entry));
        }
        Assert.Equal(2, cycle.ItemCount);
        Assert.Equal(2, seen.Count);
        Assert.DoesNotContain(later, seen);
    }

    [Fact]
    public async Task Worker_capture_reuses_the_completed_detached_snapshot()
    {
        var store = new AdmissionInventoryStore();
        store.Observe(new object(), "mod", "1");
        var publication = new AdmissionInventoryPublication();
        var completed = store.Capture();
        publication.Publish(completed, 9);
        var worker = await Task.Run(() => publication.Capture(store, 9, false, 0));
        Assert.Same(completed, worker);
        Assert.Equal("mod", Assert.Single(worker.Entries).Id);
    }

    [Fact]
    public void New_claim_requires_complete_current_publication_but_existing_generation_stays_latched()
    {
        var store = new AdmissionInventoryStore();
        store.Observe(new object(), "mod", "1");
        var publication = new AdmissionInventoryPublication();
        Assert.Throws<InvalidOperationException>(() => publication.Capture(store, 1, false, 0));
        long frozen = store.Generation;
        publication.Publish(store.Capture(), 1);
        Assert.Throws<InvalidOperationException>(() => publication.Capture(store, 1, true, 0));
        Assert.Equal(frozen, store.Generation);
        Assert.Throws<InvalidOperationException>(() => publication.Capture(store, 2, false, 0));
        Assert.Throws<InvalidOperationException>(() => publication.Capture(store, 1, false, 1));
        Assert.Same(publication.Capture(store, 1, false, 0), publication.Capture(store, 1, false, 0));
    }

    [Fact]
    public void Later_identity_observations_or_invalid_store_reject_old_worker_publication()
    {
        var store = new AdmissionInventoryStore();
        var mod = new object();
        store.Observe(mod, "mod", "1");
        var publication = new AdmissionInventoryPublication();
        publication.Publish(store.Capture(), 1);
        store.ObserveRuntimeIdentity(mod, "changed", "2");
        Assert.Throws<InvalidOperationException>(() => publication.Capture(store, 1, false, 0));
        publication.Publish(store.Capture(), 1);
        store.Invalidate("invalid source");
        Assert.Throws<InvalidOperationException>(() => publication.Capture(store, 1, false, 0));
    }

    [Fact]
    public void Completed_restoration_invalidates_prior_divergence_once_and_retains_lifetime_claim()
    {
        var store = new AdmissionInventoryStore();
        var mod = new object();
        store.Observe(mod, "mod", "1");
        var registry = Registry((mod, "mod"));
        var missing = new AdmissionRegistryReconciliation(1, registry, Array.Empty<object>());
        Assert.True(missing.TryTake(out object item));
        missing.Observe(item, true, registry[item]);
        Assert.True(missing.TryPublish(1, store));
        long divergent = store.Generation;
        var restored = new AdmissionRegistryReconciliation(1, registry, new[] { mod });
        Assert.True(restored.TryTake(out item));
        restored.Observe(item, true, registry[item]);
        Assert.True(restored.TryPublish(1, store));
        Assert.True(store.Generation > divergent);
        long complete = store.Generation;
        Assert.True(restored.TryPublish(1, store));
        Assert.Equal(complete, store.Generation);
        Assert.Equal("mod", Assert.Single(store.Capture().Entries).Id);
    }

    private static Dictionary<object, AdmissionInventoryEntry> Registry(params (object Origin, string Id)[] entries) =>
        entries.ToDictionary(entry => entry.Origin, entry => new AdmissionInventoryEntry(entry.Id, "1"), ReferenceEqualityComparer.Instance);
}
