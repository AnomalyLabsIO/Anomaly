using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Compatibility;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Input;
using Anomaly.Client.Api.Scheduling;
using Anomaly.Client.Api.UI.ModSettings;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(HelloAnomalyMod.Core), "Hello Anomaly", "0.1.0", "YourName", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: MelonAdditionalDependencies("Anomaly.Client")]
[assembly: SupportedGameVersion("14.2.7^")]

namespace HelloAnomalyMod;

public sealed class Core : MelonMod
{
    private const string ModId = "example.hello";
    // Raw MelonPreferences category IDs must avoid dotted TOML paths.
    private const string PreferenceCategoryId = "example_hello";
    private const string BindingId = ModId + ".greet";
    private IDisposable _binding;
    private ScheduledTask _heartbeat;
    private MelonPreferences_Entry<bool> _enabled;

    public override void OnInitializeMelon()
    {
        var category = MelonPreferences.CreateCategory(PreferenceCategoryId, "Hello Anomaly");
        _enabled = category.CreateEntry("enabled", true, "Enable greeting", "Allow the sample command and F6 binding to greet you.");
        ModSettings.RegisterEntry(_enabled, new ModSettingMetadata { Group = "Greetings", Order = 0 });
        CommandRegistry.Register(ModId, new HelloCommand(this));
        _binding = InputRegistry.Register(new InputBindingDefinition(BindingId, "Greeting", new InputKey(KeyCode.F6)));
        InputRegistry.Pressed += OnPressed;
        ClientEvents.Ready += OnReady;
        ClientEvents.Disconnected += OnDisconnected;
        MelonLogger.Msg("Hello Anomaly loaded. Run example.hello.hello or press F6. Settings → Mods controls the greeting.");
    }

    public override void OnDeinitializeMelon()
    {
        ClientEvents.Ready -= OnReady;
        ClientEvents.Disconnected -= OnDisconnected;
        InputRegistry.Pressed -= OnPressed;
        CancelHeartbeat();
        _binding?.Dispose();
        _binding = null;
        CommandRegistry.UnregisterAll(ModId);
    }

    private void OnPressed(string id)
    {
        if (id == BindingId && _enabled?.Value == true) MelonLogger.Msg("Hello from the sample key binding.");
    }

    private void OnReady(ClientReadyEventArgs _)
    {
        CancelHeartbeat();
        _heartbeat = Scheduler.Every(TimeSpan.FromMinutes(1), () =>
        {
            if (_enabled?.Value == true) MelonLogger.Msg("Hello Anomaly is active in this session.");
        });
    }

    private void OnDisconnected(ClientDisconnectedEventArgs _) => CancelHeartbeat();

    private void CancelHeartbeat()
    {
        _heartbeat?.Cancel();
        _heartbeat = null;
    }

    private sealed class HelloCommand : ICommand
    {
        private readonly Core _owner;
        public HelloCommand(Core owner) => _owner = owner;
        public string Name => "hello";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public string Description => "Checks the sample's command and preference.";
        public void Execute(CommandContext context) => context.Reply(_owner._enabled?.Value == true
            ? "Hello from an ordinary MelonLoader mod." : "Greeting disabled in Settings → Mods.");
    }
}
