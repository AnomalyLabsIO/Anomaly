using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Config;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Input;
using Anomaly.Client.Api.Internal.Input;
using Anomaly.Client.Api.Internal.UI;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Persistence;
using Anomaly.Client.Api.Scheduling;
using Anomaly.Client.Api.UI;
using UnityEngine;
using Xunit;

namespace Anomaly.Client.Api.Tests;

[CollectionDefinition("Registration lifecycle", DisableParallelization = true)]
public sealed class RegistrationLifecycleCollection { }

[Collection("Registration lifecycle")]
public sealed class EarlyAccessApiTests
{
    [Theory]
    [InlineData("anomaly")]
    [InlineData("anomaly.mod")]
    [InlineData("anomaly.")]
    [InlineData("UPPER")]
    [InlineData("contains space")]
    public void Public_registrations_reject_reserved_or_invalid_namespaces(string id)
    {
        Assert.Throws<ArgumentException>(() => CommandRegistry.Register(id, new TestCommand()));
        Assert.Throws<ArgumentException>(() => InputRegistry.Register(Definition(id)));
        Assert.Throws<ArgumentException>(() => VirtualInputs.RegisterButton(id, KeyCode.F, () => false));
        Assert.Throws<ArgumentException>(() => VirtualInputs.RegisterMouseButton(id, 0, () => false));
        Assert.Throws<ArgumentException>(() => VirtualInputs.RegisterAxis(id, "Horizontal", () => 0));
        Assert.Throws<ArgumentException>(() => Scheduler.StartCooldown(id, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => ClientConfig.For(id));
        Assert.Throws<ArgumentException>(() => ClientPersistence.For(id));
    }

    [Fact]
    public void Framework_commands_have_a_separate_internal_registration_path()
    {
        var command = new TestCommand();
        try
        {
            CommandRegistry.RegisterBuiltIn(command);
            Assert.Same(command, CommandRegistry.Lookup("anomaly.ea_test"));
        }
        finally
        {
            CommandRegistry.Unregister("anomaly", command.Name);
        }
    }

    [Fact]
    public void Framework_lifecycle_controls_are_not_public_contracts()
    {
        Assert.False(typeof(FileCache).IsVisible);
        Assert.False(typeof(LocalizationService).IsVisible);
        Assert.Null(typeof(AnomalyResources).GetMethod("UnloadOne"));
        Assert.Null(typeof(AnomalyResources).GetMethod("UnloadAll"));
        Assert.Null(typeof(ClientConfig).GetMethod("DisposeAll"));
        Assert.Null(typeof(ClientPersistence).GetMethod("FlushAll"));
        Assert.NotNull(typeof(AnomalyResources).GetMethod("TryGetTexture"));
        Assert.NotNull(typeof(IPersistenceStore).GetMethod("Flush"));
    }

    [Fact]
    public void Input_handle_disposal_is_idempotent_and_cannot_remove_replacement()
    {
        const string id = "tests.input.owned";
        var definition = Definition(id);
        IDisposable original = InputRegistry.Register(definition);
        IDisposable duplicate = InputRegistry.Register(Definition(id));
        duplicate.Dispose();
        Assert.Same(definition, InputRegistry.GetBinding(id));
        original.Dispose();
        Assert.Null(InputRegistry.GetBinding(id));
        Assert.False(InputRegistry.IsPressed(id));
        Assert.False(InputRegistry.WasPressedThisFrame(id));
        Assert.False(InputRegistry.WasReleasedThisFrame(id));

        var replacement = Definition(id);
        using IDisposable replacementHandle = InputRegistry.Register(replacement);
        original.Dispose();
        duplicate.Dispose();
        Assert.Same(replacement, InputRegistry.GetBinding(id));
    }

    [Fact]
    public void Disposing_during_pressed_callback_stops_later_subscribers_without_release()
    {
        const string id = "tests.input.self_disposal";
        using IDisposable handle = InputRegistry.Register(Definition(id));
        InputFrameBinding binding = InputRegistry.ResolvedKeysSnapshot().Single(binding => binding.Id == id);
        var observed = new List<string>();
        Action<string> first = _ => { observed.Add("first"); handle.Dispose(); };
        Action<string> later = _ => observed.Add("later");
        Action<string> released = _ => observed.Add("released");
        InputRegistry.Pressed += first;
        InputRegistry.Pressed += later;
        InputRegistry.Released += released;
        try
        {
            InputRegistry.OnPressed(binding);
            InputRegistry.OnReleased(binding);
            Assert.Equal(new[] { "first" }, observed);
        }
        finally
        {
            InputRegistry.Pressed -= first;
            InputRegistry.Pressed -= later;
            InputRegistry.Released -= released;
        }
    }

    [Fact]
    public void Frame_skips_disposed_entries_and_replacements_start_on_the_next_poll()
    {
        const string firstId = "tests.input.first";
        const string secondId = "tests.input.second";
        using IDisposable first = InputRegistry.Register(Definition(firstId));
        IDisposable second = InputRegistry.Register(Definition(secondId));
        IDisposable? replacement = null;
        var dispatcher = new InputFrameDispatcher();
        var observed = new List<string>();
        try
        {
            dispatcher.Update(InputRegistry.ResolvedKeysSnapshot(), _ => true, InputRegistry.IsCurrent,
                binding =>
                {
                    observed.Add(binding.Id);
                    if (binding.Id != firstId) return;
                    second.Dispose();
                    replacement = InputRegistry.Register(Definition(secondId));
                }, _ => throw new InvalidOperationException("Unexpected release."));
            Assert.Equal(new[] { firstId }, observed);

            dispatcher.Update(InputRegistry.ResolvedKeysSnapshot(), _ => true, InputRegistry.IsCurrent,
                binding => observed.Add(binding.Id), _ => throw new InvalidOperationException("Unexpected release."));
            Assert.Equal(new[] { firstId, secondId }, observed);
        }
        finally
        {
            second.Dispose();
            replacement?.Dispose();
        }
    }

    [Fact]
    public void Held_state_is_forgotten_without_synthetic_release()
    {
        var dispatcher = new InputFrameDispatcher();
        var registration = new object();
        var binding = new InputFrameBinding("tests.input.held", default, registration);
        int pressed = 0;
        int released = 0;
        dispatcher.Update(new[] { binding }, _ => true, _ => true, _ => pressed++, _ => released++);
        dispatcher.Forget(registration);
        dispatcher.Update(new[] { binding }, _ => true, _ => true, _ => pressed++, _ => released++);
        Assert.Equal(2, pressed);
        Assert.Equal(0, released);
    }

    [Fact]
    public void Canvas_layers_are_ordered_for_all_creation_orders()
    {
        UiLayer[] layers = { UiLayer.Background, UiLayer.HUD, UiLayer.Modal, UiLayer.Debug };
        foreach (UiLayer[] creationOrder in Permutations(layers))
        {
            var children = new List<UiLayer>();
            foreach (UiLayer layer in creationOrder)
            {
                children.Add(layer);
                UiLayerOrder.Apply(children.ToArray(), (current, index) =>
                {
                    children.Remove(current);
                    children.Insert(Math.Min(index, children.Count), current);
                });
                Assert.Equal(children.OrderBy(value => (int)value), children);
            }
        }
    }

    private static InputBindingDefinition Definition(string id) => new(id, id + ".name", new InputKey(KeyCode.F));

    private static IEnumerable<UiLayer[]> Permutations(UiLayer[] layers)
    {
        if (layers.Length == 0)
        {
            yield return Array.Empty<UiLayer>();
            yield break;
        }
        foreach (UiLayer first in layers)
            foreach (UiLayer[] remainder in Permutations(layers.Where(layer => layer != first).ToArray()))
                yield return new[] { first }.Concat(remainder).ToArray();
    }

    private sealed class TestCommand : ICommand
    {
        public string Name => "ea_test";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public string Description => "Early access contract test.";
        public void Execute(CommandContext context) { }
    }
}
