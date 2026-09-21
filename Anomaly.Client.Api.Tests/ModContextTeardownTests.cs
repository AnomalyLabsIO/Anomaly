using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Config;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Persistence;
using Anomaly.Client.Api.Scheduling;
using Anomaly.Client.Internal.Modding;
using Xunit;

namespace Anomaly.Client.Api.Tests;

[Collection("Registration lifecycle")]
public sealed class ModContextTeardownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Throwing_cleanup_cannot_stop_other_owned_cleanup(bool throwDuringFlush)
    {
        string root = Path.Combine(Path.GetTempPath(), "Anomaly.Context.Tests", Guid.NewGuid().ToString("N"));
        var observed = new List<string>();
        var store = new TestStore(() =>
        {
            observed.Add("flush");
            if (throwDuringFlush) throw new InvalidOperationException("Injected flush failure.");
        });
        using var context = new ModContext("tests.context", root, () => { }, () => { }, _ => { }, store, store);
        var cancellation = context.CancellationToken;
        int callbacks = 0;
        try
        {
            context.RegisterCommand(new TestCommand());
            ScheduledTask scheduled = context.Schedule(TimeSpan.Zero, () => callbacks++);
            context.Subscribe<ClientReadyEventArgs>(handler => ClientEvents.Ready += handler,
                handler => ClientEvents.Ready -= handler, _ => callbacks++);
            context.RegisterCleanup(() => observed.Add("cleanup-a"));
            context.RegisterCleanup(() =>
            {
                observed.Add("cleanup-b");
                throw new InvalidOperationException("Injected cleanup failure.");
            });
            context.Track(new TestResource(() => observed.Add("resource-a")));
            context.Track(new TestResource(() =>
            {
                observed.Add("resource-b");
                throw new InvalidOperationException("Injected resource failure.");
            }));
            context.ReportHealth("test", FeatureHealthStatus.Healthy);
            cancellation.Register(() =>
            {
                observed.Add("cancel");
                throw new InvalidOperationException("Injected cancellation failure.");
            });

            context.Dispose();
            context.Dispose();

            Assert.True(context.TeardownFailed);
            Assert.True(cancellation.IsCancellationRequested);
            Assert.False(scheduled.IsActive);
            Assert.Null(CommandRegistry.Lookup("tests.context.teardown"));
            ClientEvents.OnReady(new ClientReadyEventArgs("TestScene"));
            Scheduler.Tick(0.1);
            Assert.Equal(0, callbacks);
            Assert.DoesNotContain(FeatureHealth.Snapshot().Values, record => record.OwnerId == "tests.context");
            Assert.Equal(new[] { "flush", "cancel", "cleanup-b", "cleanup-a", "resource-b", "resource-a" }, observed);
        }
        finally
        {
            context.Dispose();
            string resolved = Path.GetFullPath(root);
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Anomaly.Context.Tests"));
            if (string.Equals(Path.GetDirectoryName(resolved), parent, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
                Directory.Delete(resolved, recursive: true);
        }
    }

    private sealed class TestResource(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class TestStore(Action flush) : IConfigSource, IPersistenceStore
    {
        public string ModId => "tests.context";
        public event Action<string> KeyChanged { add { } remove { } }
        public T Get<T>(string key, T defaultValue = default!) => defaultValue;
        public void Set<T>(string key, T value) { }
        public bool Has(string key) => false;
        public void Remove(string key) { }
        public void Save() { }
        public void Reload() { }
        public T Load<T>(string key, T defaultValue = default!) => defaultValue;
        public void Save<T>(string key, T value) { }
        public bool Exists(string key) => false;
        public void Delete(string key) { }
        public void Flush() => flush();
    }

    private sealed class TestCommand : ICommand
    {
        public string Name => "teardown";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public string Description => "Owned teardown test command.";
        public void Execute(CommandContext context) { }
    }
}
