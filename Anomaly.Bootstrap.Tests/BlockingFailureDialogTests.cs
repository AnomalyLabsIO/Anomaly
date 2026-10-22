using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class BlockingFailureDialogTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Action_waits_until_blocking_display_is_dismissed()
    {
        using var entered = new ManualResetEventSlim();
        using var dismissed = new ManualResetEventSlim();
        int displays = 0;
        int actions = 0;
        int errors = 0;
        string? shownMessage = null;
        var failure = new BlockingFailureAction(
            message =>
            {
                shownMessage = message;
                Interlocked.Increment(ref displays);
                entered.Set();
                if (!dismissed.Wait(Timeout))
                    throw new TimeoutException("The test did not dismiss the warning.");
            },
            () => Interlocked.Increment(ref actions),
            _ => Interlocked.Increment(ref errors));

        Task execution = Task.Run(() => failure.Run("Bootstrap warning"));
        try
        {
            Assert.True(entered.Wait(Timeout), "The warning was not displayed.");
            Assert.Equal("Bootstrap warning", shownMessage);
            Assert.True(failure.IsActive);
            Assert.Equal(0, Volatile.Read(ref actions));
            Assert.False(execution.IsCompleted);

            failure.Run("Duplicate warning");
            Assert.Equal(1, Volatile.Read(ref displays));
            Assert.Equal(0, Volatile.Read(ref actions));
        }
        finally
        {
            dismissed.Set();
            await execution.WaitAsync(Timeout);
        }

        Assert.Equal(1, actions);
        Assert.Equal(0, errors);
    }

    [Fact]
    public void Display_failure_is_reported_before_completing_action()
    {
        var displayError = new InvalidOperationException("Display unavailable");
        Exception? reportedError = null;
        var order = new List<string>();
        var failure = new BlockingFailureAction(
            _ => throw displayError,
            () => order.Add("action"),
            error =>
            {
                reportedError = error;
                order.Add("report");
            });

        failure.Run("Bootstrap warning");

        Assert.Same(displayError, reportedError);
        Assert.Equal(new[] { "report", "action" }, order);
    }

    [Fact]
    public void Reporting_failure_still_completes_action()
    {
        int actions = 0;
        var failure = new BlockingFailureAction(
            _ => throw new InvalidOperationException("Display unavailable"),
            () => actions++,
            _ => throw new InvalidOperationException("Logging unavailable"));

        Exception? error = Record.Exception(() => failure.Run("Bootstrap warning"));

        Assert.Null(error);
        Assert.Equal(1, actions);
    }

    [Fact]
    public void Reentrant_and_later_calls_display_and_complete_only_once()
    {
        int displays = 0;
        int actions = 0;
        BlockingFailureAction? failure = null;
        failure = new BlockingFailureAction(
            _ =>
            {
                displays++;
                failure!.Run("Reentrant warning");
            },
            () => actions++,
            _ => { });

        failure.Run("Bootstrap warning");
        failure.Run("Later warning");

        Assert.Equal(1, displays);
        Assert.Equal(1, actions);
    }
}
