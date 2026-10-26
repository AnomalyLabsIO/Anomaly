using System.Reflection;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Events;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class LifecycleSurfaceTests
{
    [Fact]
    public void Only_explicitly_annotated_api_types_and_members_have_approved_policies()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        var types = typeof(Player).Assembly.GetTypes();
        var annotated = types.Where(type => type.IsDefined(typeof(ScriptTypeAttribute), false) &&
            !type.IsDefined(typeof(ScriptIgnoreAttribute), false)).ToArray();
        Assert.Equal(annotated.Select(type => type.FullName).OrderBy(name => name), runtime.GetCatalog().Types
            .Where(type => type.ClrType.StartsWith("Anomaly.Client.Api.", StringComparison.Ordinal))
            .Select(type => type.ClrType).OrderBy(name => name));
        foreach (var type in annotated)
        {
            var policy = runtime.Bindings.GetTypePolicy(type);
            foreach (var member in policy.Members.Values)
            {
                var declaration = (MemberInfo?)member.Property ?? member.Method!;
                Assert.True(declaration.IsDefined(typeof(ScriptVisibleAttribute), false));
                Assert.False(declaration.IsDefined(typeof(ScriptIgnoreAttribute), false));
                Assert.True(declaration.DeclaringType!.IsDefined(typeof(ScriptTypeAttribute), false));
            }
        }
        Assert.Contains(runtime.GetCatalog().Types.Single(type => type.Name == "PlayerAddedEventArgs").Members, member => member.Name == "Player" && !member.AllowWrite);
        Assert.DoesNotContain(runtime.GetCatalog().Types.SelectMany(type => type.Members), member => member.AllowWrite);
    }

    [Fact]
    public void Cancellable_events_are_frozen_notifications_not_a_delayed_hook_veto()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        using var session = runtime.CreateDevelopmentSession();
        Assert.True(session.Load("Events.Player.Interacting.Add(function(a) allowed = a.IsAllowed; " +
            "writable = pcall(function() a.IsAllowed = false end) end); function read() return allowed and not writable end").Succeeded);
        var args = new PlayerInteractingEventArgs(null!);
        typeof(PlayerEvents).GetMethod("OnInteracting", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { args });
        args.IsAllowed = false;
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal(true, session.Invoke("read").Value);
    }

    [Fact]
    public void Nullable_observations_and_struct_snapshots_preserve_unavailable_values()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        var script = new Script(CoreModules.Basic);
        var first = ScriptEventSnapshotPlan.Create(typeof(Scp079AuxiliaryPowerChangedEventArgs), runtime.Bindings);
        script.Globals.Set("initial", ScriptFrozenEventValue.ToScriptValue(script, first.Capture(new Scp079AuxiliaryPowerChangedEventArgs(null!, null, 12))));
        script.Globals.Set("next", ScriptFrozenEventValue.ToScriptValue(script, first.Capture(new Scp079AuxiliaryPowerChangedEventArgs(null!, 12, 13))));
        var firearm = ScriptEventSnapshotPlan.Create(typeof(FirearmStateChangedEventArgs), runtime.Bindings);
        script.Globals.Set("firearm", ScriptFrozenEventValue.ToScriptValue(script, firearm.Capture(new FirearmStateChangedEventArgs(null!, null, default))));
        Assert.True(script.DoString("return initial.Previous == nil and initial.IsInitialObservation and next.Previous == 12 " +
            "and not next.IsInitialObservation and firearm.Previous == nil and firearm.Current.MagazineAmmo == nil").Boolean);
        var nullable = new ScriptBindingRegistry();
        nullable.RegisterType<NullableValues>();
        nullable.SetGlobal(script, "values", new NullableValues());
        Assert.True(script.DoString("return values.Missing == nil and values.Present == 5").Boolean);
    }

    [Fact]
    public void Registered_subclasses_project_only_the_declared_surface_in_calls_and_snapshots()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<ProjectionOwner>();
        registry.RegisterType<ProjectionDerived>();
        registry.RegisterType<ProjectionOwnerData>();
        var script = new Script(CoreModules.Basic);
        registry.SetGlobal(script, "owner", new ProjectionOwner());
        Assert.True(script.DoString("return owner.Value.Number == 7 and owner.Value.Extra == nil and owner:Read(owner.Value) == 7").Boolean);
        var snapshot = ScriptEventSnapshotPlan.Create(typeof(ProjectionOwnerData), registry);
        script.Globals.Set("frozen", ScriptFrozenEventValue.ToScriptValue(script, snapshot.Capture(new ProjectionOwnerData())));
        Assert.True(script.DoString("return frozen.Value.Number == 7 and frozen.Value.Extra == nil").Boolean);
    }

    [Fact]
    public void Destroyed_wrapper_snapshots_do_not_dereference_stale_native_properties()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<DestroyedValue>();
        var snapshot = Assert.IsType<ScriptFrozenEventValue>(ScriptEventSnapshotPlan.Create(typeof(DestroyedValue), registry).Capture(new DestroyedValue()));
        Assert.Equal(true, snapshot.Fields["IsDestroyed"]);
        Assert.Null(snapshot.Fields["Stale"]);
    }

    [Fact]
    public void Game_queries_are_installed_without_running_getters_or_exposing_native_handles()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        using var session = runtime.CreateDevelopmentSession();
        Assert.True(session.Load("function check() return Game ~= nil and Game.GetPlayer ~= nil and Game.GetType == nil and Game.Transform == nil end").Succeeded);
        Assert.Equal(true, session.Invoke("check").Value);
        Assert.Throws<ArgumentException>(() => session.SetGlobal("Game", null));
        Assert.True(runtime.GetCatalog().HasGameGlobal);
    }

    [Fact]
    public void Only_annotated_events_are_exposed_and_input_strings_use_the_normal_category_name()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        Assert.DoesNotContain(runtime.GetCatalog().Events, entry => entry.ClrDeclaringType == typeof(CameraManager).FullName);
        Assert.DoesNotContain(runtime.GetCatalog().Events, entry => entry.ClrDeclaringType == typeof(Api.Networking.AnomalyMessaging).FullName);
        using var session = runtime.CreateDevelopmentSession();
        var loaded = session.Load("Events.InputRegistry.Pressed.Add(function(id) pressed=id end); " +
            "function read() return pressed == 'example.action' end");
        Assert.True(loaded.Succeeded, loaded.Message);
        var input = (Delegate)typeof(Api.Input.InputRegistry).GetField("Pressed", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        input.DynamicInvoke("example.action");
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal(true, session.Invoke("read").Value);
    }

    [Fact]
    public void Type_annotations_are_required_on_each_subclass_and_on_shared_member_owners()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<ProjectionDerived>();
        Assert.Contains("Number", registry.GetTypePolicy(typeof(ProjectionDerived)).Members.Keys);
        Assert.Throws<InvalidOperationException>(() => registry.RegisterType<UnannotatedProjection>());
        registry.RegisterType<AnnotatedChildOfUnannotatedParent>();
        Assert.Empty(registry.GetTypePolicy(typeof(AnnotatedChildOfUnannotatedParent)).Members);
        Assert.Throws<InvalidOperationException>(() => registry.RegisterType<EventArgs>());
    }

    [Fact]
    public void Unmarked_or_ignored_overrides_do_not_reexpose_the_annotated_base_declaration()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<Overrides>();
        Assert.Empty(registry.GetTypePolicy(typeof(Overrides)).Members);
    }

    [ScriptType] public class ProjectionBase { [ScriptVisible] public int Number => 7; }
    [ScriptType] public sealed class ProjectionDerived : ProjectionBase { [ScriptVisible] public int Extra => 9; }
    public sealed class UnannotatedProjection : ProjectionBase { }
    public class UnannotatedParent { [ScriptVisible] public int Hidden => 3; }
    [ScriptType] public sealed class AnnotatedChildOfUnannotatedParent : UnannotatedParent { }
    [ScriptType] public class VirtualMembers
    {
        [ScriptVisible] public virtual int Number => 1;
        [ScriptVisible] public virtual int Read() => 2;
    }
    [ScriptType] public sealed class Overrides : VirtualMembers
    {
        [ScriptVisible, ScriptIgnore] public override int Number => 3;
        public override int Read() => 4;
    }
    [ScriptType] public sealed class ProjectionOwner
    {
        [ScriptVisible] public ProjectionBase Value => new ProjectionDerived();
        [ScriptVisible] public int Read(ProjectionBase value) => value.Number;
    }
    [ScriptType] public sealed class ProjectionOwnerData { [ScriptVisible] public ProjectionBase Value => new ProjectionDerived(); }
    [ScriptType] public sealed class NullableValues
    {
        [ScriptVisible] public int? Missing => null;
        [ScriptVisible] public int? Present => 5;
    }
    [ScriptType] public sealed class DestroyedValue
    {
        [ScriptVisible] public bool IsDestroyed => true;
        [ScriptVisible] public string Stale => throw new InvalidOperationException("Destroyed native handle");
    }
}
