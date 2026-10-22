using System;
using System.Threading;
using System.Threading.Tasks;
using Anomaly.Client.Auth;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class AdmissionSendGateTests
{
    [Fact]
    public async Task LateWorkerFailureCannotPoisonNewConnectionOrHostScope()
    {
        var gate = new AdmissionSendGate<object>();
        var old = gate.Current;
        old.Bind(new object());
        using (var captured = new ManualResetEventSlim())
        using (var resume = new ManualResetEventSlim())
        {
            var worker = Task.Run(() =>
            {
                var scope = gate.Current;
                Assert.Same(old, scope);
                captured.Set();
                Assert.True(resume.Wait(TimeSpan.FromSeconds(10)));
                scope.Fail("old send failed");
            });
            Assert.True(captured.Wait(TimeSpan.FromSeconds(10)));
            gate.BeginScope();
            var current = gate.Current;
            var claim = new object();
            current.Bind(claim);
            resume.Set();
            Assert.Same(worker, await Task.WhenAny(worker, Task.Delay(TimeSpan.FromSeconds(10))));
            await worker;

            Assert.Equal("old send failed", old.Failure);
            Assert.False(gate.IsCurrent(old));
            Assert.False(gate.TryBeginFailureHandling(old, out _));
            Assert.True(gate.IsCurrent(current));
            Assert.Null(current.Failure);
            Assert.Same(claim, current.Admission);
        }
    }

    [Fact]
    public void TeardownKeepsDenialLatchedUntilExplicitNewScope()
    {
        var gate = new AdmissionSendGate<object>();
        var failed = gate.Current;
        failed.Bind(new object());
        failed.Fail("audit changed");
        Assert.True(gate.TryBeginFailureHandling(failed, out var reason));
        Assert.Equal("audit changed", reason);
        failed.ClearAdmission();
        Assert.Null(failed.Admission);
        Assert.Equal("audit changed", gate.Current.Failure);
        Assert.False(gate.TryBeginFailureHandling(failed, out _));

        gate.BeginScope();
        Assert.Null(gate.Current.Admission);
        Assert.Null(gate.Current.Failure);
        Assert.Equal("audit changed", failed.Failure);
    }

    [Fact]
    public void OneScopeFreezesOneClaimAndPreservesFirstFailure()
    {
        var gate = new AdmissionSendGate<object>();
        var scope = gate.Current;
        var admission = new object();
        Assert.Throws<ArgumentNullException>(() => scope.Bind(null));
        scope.Bind(admission);
        Assert.Throws<InvalidOperationException>(() => scope.Bind(new object()));
        Assert.Same(admission, scope.Admission);
        scope.Fail("first cause");
        scope.Fail("later symptom");
        Assert.Equal("first cause", scope.Failure);
        scope.ClearAdmission();
        Assert.Throws<InvalidOperationException>(() => scope.Bind(new object()));
    }

    [Fact]
    public void ConnectionWriterMustBePreparedInTheCurrentScope()
    {
        var gate = new AdmissionSendGate<object>();
        var first = gate.Current;
        Assert.False(first.IsPreparedWriter(123));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.PrepareWriter(0));
        first.PrepareWriter(123);
        Assert.True(first.IsPreparedWriter(123));
        Assert.False(first.IsPreparedWriter(456));

        gate.BeginScope();
        Assert.False(gate.Current.IsPreparedWriter(123));
        gate.Current.PermitLocalHost();
        Assert.False(gate.Current.IsPreparedWriter(456));
        Assert.True(gate.Current.IsPreparedWriter(456, localHostActive: true));
        gate.Current.RevokePreparedWriter();
        Assert.False(gate.Current.IsPreparedWriter(456));
    }
}
