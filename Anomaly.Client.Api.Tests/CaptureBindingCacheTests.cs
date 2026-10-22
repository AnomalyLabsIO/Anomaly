using Anomaly.Client.Api.Internal.Poses;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class CaptureBindingCacheTests
{
    private sealed class Incarnation
    {
        internal int Model, Life, Schema;
        internal bool Pooled, DestroyedBone;
    }

    [Fact]
    public void ReusesOnlyTheCurrentIncarnation()
    {
        int model = 1, life = 2, schema = 3, resolutions = 0;
        var cache = new CaptureBindingCache<int, Incarnation>(b =>
            b.Model == model && b.Life == life && b.Schema == schema && !b.Pooled && !b.DestroyedBone);
        Incarnation Create(int _) { resolutions++; return new() { Model = model, Life = life, Schema = schema }; }
        Assert.True(cache.TryGetOrCreate(10, Create, out var first));
        for (int i = 0; i < 30; i++)
        {
            Assert.True(cache.TryGetOrCreate(10, Create, out var next));
            Assert.Same(first, next);
        }
        Assert.Equal(1, resolutions);
        model++;
        Assert.True(cache.TryGetOrCreate(10, Create, out var replaced));
        Assert.NotSame(first, replaced);
        life++;
        Assert.True(cache.TryGetOrCreate(10, Create, out replaced));
        schema++;
        Assert.True(cache.TryGetOrCreate(10, Create, out replaced));
        replaced.Pooled = true;
        Assert.True(cache.TryGetOrCreate(10, Create, out replaced));
        replaced.DestroyedBone = true;
        Assert.True(cache.TryGetOrCreate(10, Create, out replaced));
        Assert.Equal(6, resolutions);
    }

    [Fact]
    public void ClearDropsAllReferencesAndForcesResolution()
    {
        var cache = new CaptureBindingCache<int, Incarnation>(_ => true);
        Assert.True(cache.TryGetOrCreate(1, _ => new(), out var first));
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.True(cache.TryGetOrCreate(1, _ => new(), out var next));
        Assert.NotSame(first, next);
    }

    [Fact]
    public void PruneRemovesRetiredModelsWithoutAReplacementCapture()
    {
        var cache = new CaptureBindingCache<int, Incarnation>(b => !b.Pooled);
        cache.TryGetOrCreate(1, _ => new(), out var retired);
        cache.TryGetOrCreate(2, _ => new(), out _);
        retired.Pooled = true;
        cache.Prune();
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void FailedBindingIsNotRetained()
    {
        var cache = new CaptureBindingCache<int, Incarnation>(_ => true);
        Assert.False(cache.TryGetOrCreate(1, _ => null!, out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void RetentionIsBoundedEvenWithoutLifecycleEvents()
    {
        var cache = new CaptureBindingCache<int, Incarnation>(_ => true, 2);
        for (int i = 0; i < 20; i++)
        {
            Assert.True(cache.TryGetOrCreate(i, _ => new(), out _));
            Assert.InRange(cache.Count, 1, 2);
        }
    }
}
