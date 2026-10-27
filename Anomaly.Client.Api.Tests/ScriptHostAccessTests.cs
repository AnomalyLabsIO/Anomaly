using Anomaly.Client.Api.Internal.Wrappers;
using Anomaly.Client.Api.Scripting;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class ScriptHostAccessTests
{
    [Fact]
    public void Trusted_calls_outside_a_scope_keep_existing_unbounded_behavior()
    {
        Assert.False(ScriptHostAccess.IsActive);
        ScriptHostAccess.CheckNativeStringLength(int.MaxValue);
        ScriptHostAccess.ChargeRegistryEntries(int.MaxValue);
        for (int i = 0; i < 128; ++i)
        {
            ScriptHostAccess.ChargeHierarchyStep();
            ScriptHostAccess.ChargeWrapperCreation();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    public void Native_string_preflight_accepts_the_boundary(int length)
    {
        using var scope = ScriptHostAccess.Enter();
        ScriptHostAccess.CheckNativeStringLength(length);
        ScriptHostAccess.ThrowIfFailed();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4097)]
    [InlineData(int.MaxValue)]
    public void Native_string_preflight_fails_before_the_copy_can_start(int length)
    {
        using var scope = ScriptHostAccess.Enter();
        bool copied = false;
        Assert.Throws<ScriptHostAccessException>(() =>
        {
            ScriptHostAccess.CheckNativeStringLength(length);
            copied = true;
        });
        Assert.False(copied);
        Assert.Throws<ScriptHostAccessException>(() => ScriptHostAccess.CheckNativeStringLength(1));
        Assert.Throws<ScriptHostAccessException>(ScriptHostAccess.ThrowIfFailed);
    }

    [Fact]
    public void Hierarchy_steps_fail_on_the_sixty_fifth_step()
    {
        using var scope = ScriptHostAccess.Enter();
        for (int i = 0; i < 64; ++i)
            ScriptHostAccess.ChargeHierarchyStep();
        Assert.Throws<ScriptHostAccessException>(ScriptHostAccess.ChargeHierarchyStep);
    }

    [Fact]
    public void Wrapper_creation_fails_before_the_fifth_creation()
    {
        using var scope = ScriptHostAccess.Enter();
        for (int i = 0; i < 4; ++i)
            ScriptHostAccess.ChargeWrapperCreation();
        Assert.Throws<ScriptHostAccessException>(ScriptHostAccess.ChargeWrapperCreation);
    }

    [Fact]
    public void Registry_scans_and_growth_share_one_aggregate_limit()
    {
        using var scope = ScriptHostAccess.Enter();
        ScriptHostAccess.ChargeRegistryEntries(511);
        ScriptHostAccess.ChargeRegistryEntries();
        Assert.Throws<ScriptHostAccessException>(() => ScriptHostAccess.ChargeRegistryEntries());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(513)]
    [InlineData(int.MaxValue)]
    public void Invalid_or_oversized_registry_preflight_fails_closed(int count)
    {
        using var scope = ScriptHostAccess.Enter();
        Assert.Throws<ScriptHostAccessException>(() => ScriptHostAccess.ChargeRegistryEntries(count));
    }

    [Fact]
    public void Nested_scopes_cannot_reset_work_or_a_terminal_failure()
    {
        using var outer = ScriptHostAccess.Enter();
        ScriptHostAccess.ChargeRegistryEntries(512);
        using (ScriptHostAccess.Enter())
            Assert.Throws<ScriptHostAccessException>(() => ScriptHostAccess.ChargeRegistryEntries());
        Assert.Throws<ScriptHostAccessException>(() => ScriptHostAccess.Enter());
        Assert.Throws<ScriptHostAccessException>(ScriptHostAccess.ThrowIfFailed);
    }

    [Fact]
    public void A_new_operation_resets_counters_and_clears_the_disposed_failure()
    {
        using (ScriptHostAccess.Enter())
            Assert.Throws<ScriptHostAccessException>(() => ScriptHostAccess.ChargeRegistryEntries(513));
        Assert.False(ScriptHostAccess.IsActive);
        using var next = ScriptHostAccess.Enter();
        ScriptHostAccess.ChargeRegistryEntries(512);
        ScriptHostAccess.CheckNativeStringLength(4096);
        ScriptHostAccess.ThrowIfFailed();
    }

    [Fact]
    public void Scope_is_thread_local_and_does_not_limit_unrelated_trusted_work()
    {
        using var scope = ScriptHostAccess.Enter();
        ScriptHostAccess.ChargeRegistryEntries(512);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.False(ScriptHostAccess.IsActive);
                ScriptHostAccess.ChargeRegistryEntries(int.MaxValue);
                using var independent = ScriptHostAccess.Enter();
                ScriptHostAccess.ChargeRegistryEntries(512);
            }
            catch (Exception error) { failure = error; }
        });
        thread.Start();
        thread.Join();
        Assert.Null(failure);
        Assert.Throws<ScriptHostAccessException>(() => ScriptHostAccess.ChargeRegistryEntries());
    }

    [Fact]
    public void Serial_index_growth_rejects_before_mutation_but_existing_owner_updates_work()
    {
        var index = new SerialOwnershipIndex<object>();
        var original = new object();
        for (ushort serial = 1; serial <= 513; ++serial)
            index.Assign(serial, original);
        using (ScriptHostAccess.Enter())
        {
            var replacement = new object();
            Assert.Same(original, index.Assign(1, replacement));
            Assert.True(index.TryGet(1, out var current));
            Assert.Same(replacement, current);
            Assert.Throws<ScriptHostAccessException>(() => index.Assign(514, replacement));
            Assert.False(index.TryGet(514, out _));
        }
        Assert.Null(index.Assign(514, original));
        Assert.True(index.TryGet(514, out _));
    }

    [Fact]
    public void Scope_disposal_requires_stack_order_and_does_not_clear_an_outer_scope()
    {
        var outer = ScriptHostAccess.Enter();
        var inner = ScriptHostAccess.Enter();
        Assert.Throws<InvalidOperationException>(() => outer.Dispose());
        Assert.True(ScriptHostAccess.IsActive);
        inner.Dispose();
        outer.Dispose();
        Assert.False(ScriptHostAccess.IsActive);
    }
}
