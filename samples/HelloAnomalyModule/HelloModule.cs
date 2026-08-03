using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Modding;

namespace HelloAnomalyModule;

public sealed class HelloModule : IAnomalyMod
{
    private IModContext? _context;

    public void Initialize(IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        context.RegisterCommand(new HelloCommand());
        context.ReportHealth("example", FeatureHealthStatus.Healthy, "Ready.");
    }

    public void OnEnable()
    {
        _context?.ScheduleRecurring(
            TimeSpan.FromMinutes(5),
            () => _context?.ReportHealth("example", FeatureHealthStatus.Healthy, "Heartbeat."));
    }

    public void OnDisable()
    {
        // Commands, schedules, cancellation, and tracked resources are removed
        // by the framework-owned context after this callback returns.
    }

    public void Dispose()
    {
        _context = null;
    }

    private sealed class HelloCommand : ICommand
    {
        public string Name => "hello";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public string Description => "Checks that the sample managed module is running.";

        public void Execute(CommandContext context) =>
            context.Reply("Hello from an Anomaly-owned module.");
    }
}
