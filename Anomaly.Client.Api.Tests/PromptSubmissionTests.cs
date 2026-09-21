using Anomaly.Client.Api.Internal;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class PromptSubmissionTests
{
    [Fact]
    public void Validation_or_save_error_retains_the_prompt_until_success()
    {
        var gate = new PromptLeaseGate();
        var lease = gate.Begin("nickname");
        Assert.False(gate.TrySubmit(lease, () => "Name is invalid", out var validationError));
        Assert.Equal("Name is invalid", validationError);
        Assert.True(gate.IsCurrent(lease));
        Assert.False(gate.TrySubmit(lease, () => "Save failed", out var saveError));
        Assert.Equal("Save failed", saveError);
        Assert.True(gate.IsCurrent(lease));
        Assert.True(gate.TrySubmit(lease, () => null!, out var successError));
        Assert.Null(successError);
        Assert.False(gate.IsCurrent(lease));
    }

    [Fact]
    public void Replaced_or_dismissed_prompt_cannot_submit()
    {
        var gate = new PromptLeaseGate();
        var stale = gate.Begin("nickname");
        var next = gate.Begin("connection-failure");
        Assert.False(gate.TrySubmit(stale, () => throw new Exception("Stale callback ran"), out _));
        Assert.True(gate.IsCurrent(next));
        gate.InvalidateAll();
        Assert.False(gate.TrySubmit(next, () => throw new Exception("Dismissed callback ran"), out _));
    }

    [Fact]
    public void Submission_cannot_consume_a_successor_opened_by_its_callback()
    {
        var gate = new PromptLeaseGate();
        var original = gate.Begin("nickname");
        PromptLease successor = default;
        Assert.False(gate.TrySubmit(original, () =>
        {
            successor = gate.Begin("authentication");
            return null!;
        }, out _));
        Assert.True(gate.IsCurrent(successor));
    }

    [Fact]
    public void Throwing_submission_does_not_consume_lease()
    {
        var gate = new PromptLeaseGate();
        var lease = gate.Begin("nickname");
        Assert.Throws<IOException>(() => gate.TrySubmit(lease, () => throw new IOException("Read-only profile"), out _));
        Assert.True(gate.IsCurrent(lease));
    }
}
