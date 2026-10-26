using System.Globalization;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class MemoryStringTests
{
    [Theory]
    [InlineData("return string.rep('ab',3,'-')", "ab-ab-ab")]
    [InlineData("return string.rep('ab',0,'-')", "")]
    [InlineData("return string.reverse('a'..string.char(0)..'b')", "b\0a")]
    [InlineData("return string.sub('abcdef',-4,-2)", "cde")]
    [InlineData("return string.upper('Ab09')..':'..string.lower('aB09')", "AB09:ab09")]
    [InlineData("return string.char(65,0,90)", "A\0Z")]
    [InlineData("return string.format('%s:%04d:%X','xy',7,255)", "xy:0007:FF")]
    [InlineData("return string.format('%q',string.char(0)..'7')", "\"\\0007\"")]
    [InlineData("return string.format('%q',string.char(1)..'x')", "\"\\1x\"")]
    [InlineData("return string.format('%q',string.char(15)..'7')", "\"\\0157\"")]
    [InlineData("return string.format('%q','a'..string.char(34)..'b'..string.char(92)..'c')", "\"a\\\"b\\\\c\"")]
    [InlineData("return string.format('%q','a'..string.char(10)..'b'..string.char(13))", "\"a\\\nb\\r\"")]
    [InlineData("local s,n=string.gsub('item-12 and item-34','(%a+)%-(%d+)','%2:%1'); return s..'|'..n", "12:item and 34:item|2")]
    [InlineData("local s,n=string.gsub('a1 b22','(%a)(%d+)',function(a,b) return a..':'..tostring(tonumber(b)+1) end); return s..'|'..n", "a:2 b:23|2")]
    [InlineData("local s,n=string.gsub('a b a','%a',{a='A',b='B'}); return s..'|'..n", "A B A|3")]
    [InlineData("local s,n=string.gsub('a'..string.char(0)..'b',string.char(0),'!'); return s..'|'..n", "a!b|1")]
    [InlineData("return string.match('a'..string.char(0)..'b','a'..string.char(0)..'b')", "a\0b")]
    [InlineData("local s=''; for a,b in string.gmatch('b2 c3','(%a)(%d)') do s=s..a..b end; return s", "b2c3")]
    [InlineData("return (string.gsub('a1','%d',function() return nil end))", "a1")]
    public void BoundedStringOperationsRetainTheirExactResultsWithAllocationAccounting(string source, string expected)
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(Options());
        var before = session.MemoryChargedBytes;
        var result = session.Load(source);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(expected, result.Value);
        Assert.True(session.MemoryChargedBytes > before);
        Assert.False(session.IsRetired);
    }

    [Fact]
    public void MaximumTwoDigitFloatPrecisionPreservesFractionAndLargeFiniteNumberOutput()
    {
        using var runtime = new ScriptRuntime();
        using var first = runtime.CreateDevelopmentSession(Options());
        var fraction = first.Load("return string.format('%99.99f',1.5)");
        Assert.True(fraction.Succeeded, fraction.Message);
        Assert.Equal("1.5" + new string('0', 98), fraction.Value);

        using var second = runtime.CreateDevelopmentSession(Options());
        var large = second.Load("return string.format('%99.99f',-1e308)");
        Assert.True(large.Succeeded, large.Message);
        Assert.Equal((-1e308).ToString("f99", CultureInfo.InvariantCulture), large.Value);
    }

    [Fact]
    public void BoundedGsubExpansionCannotHideMemoryTerminationInsidePcall()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(Options() with { AllocationLimitBytes = 1024 * 1024 });
        Assert.True(session.Load("local input=string.rep('a',512); local replacement=string.rep('b',512); " +
            "function Work() return pcall(function() return string.gsub(input,'a',replacement) end) end").Succeeded);
        var before = session.MemoryChargedBytes;
        var result = session.Invoke("Work");
        Assert.Equal(ScriptExecutionStatus.MemoryLimit, result.Status);
        Assert.True(session.IsRetired);
        Assert.InRange(session.MemoryChargedBytes, before + 1, 1024 * 1024);
        Assert.Equal(ScriptExecutionStatus.Retired, session.Invoke("Work").Status);
    }

    private static ScriptExecutionOptions Options() => new()
    {
        AllocationLimitBytes = 16 * 1024 * 1024,
        InstructionLimit = 2_000_000,
        Timeout = TimeSpan.FromSeconds(1)
    };
}
