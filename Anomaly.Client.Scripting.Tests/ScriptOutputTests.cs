using Anomaly.Client.Scripting.Output;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ScriptOutputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preload_source_identity_changes_apply_to_script_logs(bool explicitIdentity)
    {
        var logs = new List<string>();
        using var runtime = new ScriptRuntime(outputLog: logs.Add);
        using var session = runtime.CreateDevelopmentSession();
        if (explicitIdentity) session.SetSourceIdentity("mods/example/start.lua");
        var loaded = session.Load("assert(Output:Log('initialized'))",
            explicitIdentity ? null : "mods/example/start.lua");
        Assert.True(loaded.Succeeded, loaded.Message);
        Assert.Contains("mods/example/start.lua", Assert.Single(logs));
    }

    [Fact]
    public void Owners_expire_and_retire_independently_and_released_slots_are_reusable()
    {
        var clock = new Clock { Time = 100 };
        var sink = new RecordingSink();
        using var host = new ScriptOutputHost(textSink: sink, monotonicSeconds: () => clock.Time);
        using var first = host.CreateOutput(new Script(CoreModules.None), "first");
        using var second = host.CreateOutput(new Script(CoreModules.None), "second");
        Assert.True(first.ShowText("first", 0.1));
        Assert.True(second.ShowText("second", 10));
        clock.Time = 100.099;
        host.Tick();
        Assert.Equal(2, sink.Visible.Count);
        clock.Time = 100.1;
        host.Tick();
        Assert.Equal(new[] { "second" }, sink.Visible.Values);
        using var third = host.CreateOutput(new Script(CoreModules.None), "third");
        Assert.True(third.ShowText("third", 2));
        first.Dispose(); // A retired old owner cannot clear a reused slot.
        Assert.Contains("third", sink.Visible.Values);
        third.ClearText();
        Assert.Equal(new[] { "second" }, sink.Visible.Values);
        second.Dispose();
        Assert.Empty(sink.Visible);
    }

    [Fact]
    public void Eight_simultaneous_owners_bound_the_sink_without_limiting_logging()
    {
        var sink = new RecordingSink();
        var logs = new List<string>();
        using var host = new ScriptOutputHost(logs.Add, sink, () => 100);
        var owners = Enumerable.Range(0, 9).Select(i => host.CreateOutput(new Script(CoreModules.None), i.ToString())).ToArray();
        try
        {
            for (var i = 0; i < 8; i++) Assert.True(owners[i].ShowText(i.ToString(), 10));
            Assert.False(owners[8].ShowText("ninth", 10));
            Assert.True(owners[8].Log("still available"));
            Assert.Equal(8, sink.Visible.Count);
            owners[0].ClearText();
            // Capacity is now free, but the shared eight-updates/second window
            // still prevents another owner from multiplying native UI churn.
            Assert.False(owners[8].ShowText("runtime window full", 10));
        }
        finally { foreach (var owner in owners) owner.Dispose(); }
    }

    [Fact]
    public void Clearing_reclaims_capacity_after_the_runtime_window_resets()
    {
        var clock = new Clock { Time = 100 };
        var sink = new RecordingSink();
        using var host = new ScriptOutputHost(textSink: sink, monotonicSeconds: () => clock.Time);
        var owners = Enumerable.Range(0, 9).Select(_ => host.CreateOutput(new Script(CoreModules.None), "owner")).ToArray();
        try
        {
            for (var i = 0; i < 8; i++) Assert.True(owners[i].ShowText("held", 10));
            clock.Time = 101;
            Assert.False(owners[8].ShowText("denied capacity", 10));
            owners[0].ClearText();
            Assert.True(owners[8].ShowText("reused", 10));
            owners[0].Dispose();
            Assert.Contains("reused", sink.Visible.Values);
        }
        finally { foreach (var owner in owners) owner.Dispose(); }
    }

    [Fact]
    public void Session_and_runtime_log_rates_are_independent_from_display_rates()
    {
        var clock = new Clock { Time = 100 };
        var logs = new List<string>();
        var sink = new RecordingSink();
        using var host = new ScriptOutputHost(logs.Add, sink, () => clock.Time);
        using var output = host.CreateOutput(new Script(CoreModules.None), "pack/source");
        for (var i = 0; i < 4; i++) Assert.True(output.Log("log"));
        Assert.False(output.Log("session denied"));
        Assert.True(output.ShowText("one", 10));
        Assert.True(output.ShowText("two", 10));
        Assert.False(output.ShowText("display denied", 10));
        clock.Time = 100.999;
        Assert.False(output.Log("too early"));
        clock.Time = 101;
        Assert.True(output.Log("new window"));
        Assert.True(output.ShowText("new display window", 10));
        Assert.Equal(5, logs.Count);
    }

    [Fact]
    public void Many_sessions_cannot_multiply_the_runtime_log_cap()
    {
        var clock = new Clock { Time = 100 };
        var logs = new List<string>();
        using var host = new ScriptOutputHost(logs.Add, monotonicSeconds: () => clock.Time);
        var outputs = Enumerable.Range(0, 5).Select(_ => host.CreateOutput(new Script(CoreModules.None), "source")).ToArray();
        try
        {
            for (var i = 0; i < 4; i++)
                for (var j = 0; j < 4; j++) Assert.True(outputs[i].Log("accepted"));
            Assert.False(outputs[4].Log("aggregate denied"));
            Assert.Equal(16, logs.Count);
            clock.Time = 101;
            Assert.True(outputs[4].Log("reset"));
        }
        finally { foreach (var output in outputs) output.Dispose(); }
    }

    [Theory]
    [InlineData(0.1, true)]
    [InlineData(10, true)]
    [InlineData(0.099999, false)]
    [InlineData(10.000001, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(double.NegativeInfinity, false)]
    public void Duration_bounds_are_inclusive_and_nonfinite_values_are_rejected(double duration, bool accepted)
    {
        var sink = new RecordingSink();
        using var host = new ScriptOutputHost(textSink: sink);
        using var output = host.CreateOutput(new Script(CoreModules.None), "source");
        Assert.Equal(accepted, output.ShowText("text", duration));
        Assert.Equal(accepted ? 1 : 0, sink.Visible.Count);
    }

    [Fact]
    public void Utf8_bytes_and_lines_are_bounded_and_markup_is_delivered_literally()
    {
        var clock = new Clock { Time = 100 };
        var sink = new RecordingSink();
        using var host = new ScriptOutputHost(textSink: sink, monotonicSeconds: () => clock.Time);
        using var output = host.CreateOutput(new Script(CoreModules.None), "source");
        Assert.False(output.ShowText(new string('é', 513), 1));
        Assert.False(output.ShowText(new string('x', 1025), 1));
        Assert.False(output.ShowText("1\n2\n3\n4\n5\n6\n7\n8\n9", 1));
        Assert.False(output.ShowText("\ud800", 1));
        Assert.False(output.ShowText("", 1));
        Assert.True(output.ShowText(new string('é', 512), 1));
        Assert.Equal(new string('é', 512), Assert.Single(sink.Visible).Value);
        Assert.True(output.ShowText("1\n2\n3\n4\n5\n6\n7\n8", 1));
        clock.Time = 101;
        Assert.True(output.ShowText("<b>literal</b>", 1));
        Assert.Equal("<b>literal</b>", Assert.Single(sink.Visible).Value);
    }

    [Theory]
    [InlineData("a\rb")]
    [InlineData("a\0b")]
    [InlineData("a\u001bb")]
    [InlineData("a\u202eb")]
    [InlineData("a\u2028b")]
    public void Controls_and_log_injection_are_rejected(string text)
    {
        var logs = new List<string>();
        using var host = new ScriptOutputHost(logs.Add, new RecordingSink());
        using var output = host.CreateOutput(new Script(CoreModules.None), "pack\r\nforged");
        Assert.False(output.Log(text));
        Assert.False(output.ShowText(text, 1));
        Assert.False(output.Log("two\nlines"));
        Assert.True(output.Log("one line"));
        Assert.Equal("[Script:pack__forged] one line", Assert.Single(logs));
    }

    [Fact]
    public void Source_identity_is_bounded_without_copying_the_full_producer_string()
    {
        var logs = new List<string>();
        using var host = new ScriptOutputHost(logs.Add);
        using var output = host.CreateOutput(new Script(CoreModules.None), new string('x', 384));
        Assert.True(output.Log("message"));
        Assert.Equal("[Script:" + new string('x', 48) + "] message", Assert.Single(logs));
    }

    [Fact]
    public void Rate_denial_does_not_copy_or_charge_output_storage()
    {
        using var host = new ScriptOutputHost(_ => { }, new RecordingSink(), () => 100);
        var script = new Script(CoreModules.None);
        using var output = host.CreateOutput(script, "source");
        script.MemoryBudget = new MemoryBudget(4 * 1024 * 1024, reclaimable: true);
        for (var i = 0; i < 4; i++) Assert.True(output.Log("log"));
        Assert.True(output.ShowText("first", 10));
        Assert.True(output.ShowText("second", 10));
        var allocated = script.MemoryBudget.TotalAllocatedBytes;
        Assert.False(output.Log(new string('x', 1024)));
        Assert.False(output.ShowText(new string('x', 1024), 10));
        Assert.Equal(allocated, script.MemoryBudget.TotalAllocatedBytes);
        Assert.Equal(0, script.MemoryBudget.UnboundBytes);
    }

    [Fact]
    public void Display_memory_is_reserved_before_the_sink_gets_text()
    {
        var sink = new RecordingSink();
        using var host = new ScriptOutputHost(textSink: sink);
        var script = new Script(CoreModules.None);
        using var output = host.CreateOutput(script, "source");
        script.MemoryBudget = new MemoryBudget(1024, reclaimable: true);
        Assert.Throws<ScriptExecutionAbortedException>(() => output.ShowText("not delivered", 1));
        Assert.Empty(sink.Visible);
        Assert.Equal(0, script.MemoryBudget.UnboundBytes);
    }

    [Fact]
    public void Partial_sink_failures_cleanup_only_the_failed_owner()
    {
        var sink = new RecordingSink();
        using var host = new ScriptOutputHost(textSink: sink);
        using var first = host.CreateOutput(new Script(CoreModules.None), "first");
        using var second = host.CreateOutput(new Script(CoreModules.None), "second");
        Assert.True(first.ShowText("first", 10));
        Assert.True(second.ShowText("second", 10));
        sink.FailNextSet = true;
        Assert.False(first.ShowText("partial", 10));
        Assert.Equal(new[] { "second" }, sink.Visible.Values);
    }

    [Fact]
    public void Fatal_sink_failures_propagate_after_owner_cleanup()
    {
        var sink = new RecordingSink();
        using var host = new ScriptOutputHost(textSink: sink);
        using var first = host.CreateOutput(new Script(CoreModules.None), "first");
        using var second = host.CreateOutput(new Script(CoreModules.None), "second");
        Assert.True(first.ShowText("first", 10));
        Assert.True(second.ShowText("second", 10));
        sink.NextFailure = new ScriptExecutionAbortedException(ExecutionTerminationReason.Cancelled);
        Assert.Throws<ScriptExecutionAbortedException>(() => first.ShowText("partial", 10));
        Assert.Equal(new[] { "second" }, sink.Visible.Values);
    }

    [Fact]
    public void Lua_invalid_arguments_return_false_and_denials_do_not_prevent_later_valid_calls()
    {
        var sink = new RecordingSink();
        var logs = new List<string>();
        using var host = new ScriptOutputHost(logs.Add, sink);
        var script = new Script(CoreModules.None);
        using var output = host.CreateOutput(script, "source");
        output.InstallGlobal();
        var result = script.DoString("return Output:ShowText('bad', 0/0), Output.ShowText('bad', 1/0), Output:Log(nil), Output:Log('ok'), Output:ShowText('visible', 0.1)");
        Assert.Equal(new[] { false, false, false, true, true }, result.Tuple.Select(value => value.Boolean));
        Assert.Equal("visible", Assert.Single(sink.Visible).Value);
        Assert.True(script.DoString("return Output.Dispose == nil and Output.new == nil").Boolean);
    }

    [Fact]
    public void Disposed_capabilities_and_hosts_cannot_log_or_recreate_native_output()
    {
        var sink = new RecordingSink();
        var logs = new List<string>();
        using var host = new ScriptOutputHost(logs.Add, sink);
        var script = new Script(CoreModules.None);
        using var output = host.CreateOutput(script, "source");
        output.InstallGlobal();
        Assert.True(output.ShowText("visible", 10));
        output.Dispose();
        output.ClearText();
        Assert.False(output.Log("after dispose"));
        Assert.False(output.ShowText("after dispose", 1));
        Assert.False(script.DoString("return Output:ShowText('after dispose', 1)").Boolean);
        using var other = host.CreateOutput(new Script(CoreModules.None), "other");
        Assert.True(other.ShowText("other", 1));
        host.Dispose();
        Assert.False(other.Log("after host dispose"));
        Assert.False(other.ShowText("after host dispose", 1));
        other.ClearText();
        host.Tick();
        Assert.Empty(sink.Visible);
        Assert.Empty(logs);
        Assert.Equal(1, sink.Disposals);
    }

    [Fact]
    public void Log_accepts_exact_utf8_byte_limit_and_rejects_invalid_input_without_spending_rate()
    {
        var logs = new List<string>();
        using var host = new ScriptOutputHost(logs.Add, monotonicSeconds: () => 100);
        using var output = host.CreateOutput(new Script(CoreModules.None), "source");
        Assert.False(output.Log(new string('é', 513)));
        Assert.False(output.Log("\ud800"));
        Assert.False(output.Log(""));
        Assert.True(output.Log(new string('é', 512)));
        Assert.Equal("[Script:source] " + new string('é', 512), Assert.Single(logs));
        for (var i = 0; i < 3; i++) Assert.True(output.Log("remaining allowance"));
        Assert.False(output.Log("spent"));
    }

    [Fact]
    public void Clock_regression_cannot_resurrect_expired_text_or_reset_rate_windows()
    {
        var clock = new Clock { Time = 100 };
        var sink = new RecordingSink();
        using var host = new ScriptOutputHost(_ => { }, sink, () => clock.Time);
        using var output = host.CreateOutput(new Script(CoreModules.None), "source");
        Assert.True(output.ShowText("expiring", 1));
        clock.Time = 101;
        host.Tick();
        Assert.Empty(sink.Visible);
        for (var i = 0; i < 4; i++) Assert.True(output.Log("fills window"));
        clock.Time = 99;
        Assert.False(output.Log("backwards"));
        host.Tick();
        Assert.Empty(sink.Visible);
        clock.Time = 102;
        Assert.True(output.Log("new monotonic window"));
    }

    [Fact]
    public void Unsupported_output_is_a_normal_false_result()
    {
        using var host = new ScriptOutputHost();
        using var output = host.CreateOutput(new Script(CoreModules.None), "source");
        Assert.False(output.Log("unavailable"));
        Assert.False(output.ShowText("unavailable", 1));
        output.ClearText();
    }

    private sealed class Clock { internal double Time; }
    private sealed class RecordingSink : IScriptTextSink
    {
        internal readonly Dictionary<int, string> Visible = new();
        internal bool FailNextSet;
        internal Exception? NextFailure;
        internal int Disposals;
        public bool Set(int slot, string plainText)
        {
            Visible[slot] = plainText;
            if (NextFailure is { } failure) { NextFailure = null; throw failure; }
            if (!FailNextSet) return true;
            FailNextSet = false;
            throw new InvalidOperationException("Native creation failed after retaining the text.");
        }
        public void Clear(int slot) => Visible.Remove(slot);
        public void Dispose() { Disposals++; Visible.Clear(); }
    }
}
