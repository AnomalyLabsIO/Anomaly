using Anomaly.Shared.Scripting;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ScriptMessageRateLimiterTests
{
    [Fact]
    public void Channel_burst_and_refill_do_not_reset_on_repeated_denials()
    {
        double now = 10;
        var limiter = new ScriptMessageRateLimiter(() => now);
        for (int index = 0; index < 10; index++) Assert.True(limiter.TryAccept("example:pack", "State", 10));
        Assert.False(limiter.TryAccept("example:pack", "State", 10));
        now += 0.1;
        Assert.False(limiter.TryAccept("example:pack", "State", 10));
        now += 0.101;
        Assert.True(limiter.TryAccept("example:pack", "State", 10));
    }

    [Fact]
    public void Rotating_channels_cannot_bypass_message_or_byte_aggregate()
    {
        var messages = new ScriptMessageRateLimiter(() => 1);
        for (int index = 0; index < 40; index++) Assert.True(messages.TryAccept("example:pack", "State" + index, 1));
        Assert.False(messages.TryAccept("other:pack", "State", 1));
        var bytes = new ScriptMessageRateLimiter(() => 1);
        for (int index = 0; index < 16; index++) Assert.True(bytes.TryAccept("example:pack", "State" + index, 4096));
        Assert.False(bytes.TryAccept("other:pack", "State", 1));
    }

    [Fact]
    public void Repeated_hot_channel_attempts_share_the_aggregate_allowance()
    {
        var limiter = new ScriptMessageRateLimiter(() => 1);
        for (int index = 0; index < 40; index++) limiter.TryAccept("example:pack", "State", 1);
        Assert.False(limiter.TryAccept("example:pack", "Other", 1));
    }

    [Fact]
    public void Clock_reversal_does_not_mint_tokens_and_new_activation_gets_own_budget()
    {
        double now = 10;
        var limiter = new ScriptMessageRateLimiter(() => now);
        for (int index = 0; index < 10; index++) Assert.True(limiter.TryAccept("example:pack", "State", 1));
        now = 0;
        Assert.False(limiter.TryAccept("example:pack", "State", 1));
        now = 10;
        Assert.False(limiter.TryAccept("example:pack", "State", 1));
        Assert.True(new ScriptMessageRateLimiter(() => now).TryAccept("example:pack", "State", 1));
    }
}
