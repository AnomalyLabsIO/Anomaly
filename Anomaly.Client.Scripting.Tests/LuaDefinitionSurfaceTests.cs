using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Catalog;
using Anomaly.Client.Scripting.Events;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class LuaDefinitionSurfaceTests
{
    [Fact]
    public void Event_snapshots_use_recursive_frozen_types_and_nullable_destroyed_fields()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<SnapshotArgs>();
        var entry = new ScriptEventCatalogEntry("Test", "Removed", "Test.Events", "Removed",
            new[] { new ScriptParameterCatalog("args", typeof(SnapshotArgs).FullName!) });
        var lua = registry.GetCatalog().WithEvents(new[] { entry }).ToLuaDefinitions();

        Assert.Contains("---@param callback fun(args: EventSnapshot_SnapshotArgs|nil)", lua);
        Assert.Contains("---@field Target EventSnapshot_SnapshotTarget|nil", lua);
        var snapshot = lua[lua.IndexOf("---@class EventSnapshot_SnapshotTarget", StringComparison.Ordinal)..];
        Assert.Contains("---@field IsDestroyed boolean # Read-only.", snapshot);
        Assert.Contains("---@field Id integer|nil # Read-only.", snapshot);
        Assert.Contains("---@field Enabled boolean|nil # Read-only.", snapshot);
        Assert.Contains("---@field Id integer # Read-only.", lua);
        Assert.Contains("---@field Enabled boolean # Read-only.", lua);

        // The nullable annotation describes actual capture behavior, without reading dead native getters.
        var captured = (ScriptFrozenEventValue)ScriptEventSnapshotPlan.Create(typeof(SnapshotTarget), registry)
            .Capture(new SnapshotTarget { Destroyed = true })!;
        Assert.Equal(true, captured.Fields["IsDestroyed"]);
        Assert.Null(captured.Fields["Id"]);
        Assert.Null(captured.Fields["Enabled"]);
        new Script(CoreModules.None).LoadString(lua);
    }

    [Fact]
    public void Enum_observations_are_typed_and_flags_allow_combined_name_results()
    {
        var entries = new[]
        {
            new ScriptEnumCatalog("Game.Kind", "Kind", "Kind", false,
                new[] { new ScriptEnumMemberCatalog("None", "None", "0") }),
            new ScriptEnumCatalog("Game.Flags", "Flags", "Flags", true,
                new[] { new ScriptEnumMemberCatalog("Read", "Read", "1") })
        };
        var target = new ScriptTypeCatalog("Target", "Target", new[]
        {
            new ScriptMemberCatalog(ScriptMemberKind.Property, "Kind", "Kind", "Game.Kind", false,
                Array.Empty<ScriptParameterCatalog>(), "P:Target.Kind"),
            new ScriptMemberCatalog(ScriptMemberKind.Property, "Flags", "Flags", "Game.Flags", false,
                Array.Empty<ScriptParameterCatalog>(), "P:Target.Flags"),
            new ScriptMemberCatalog(ScriptMemberKind.Method, "Echo", "Echo", "Game.Kind", false,
                new[] { new ScriptParameterCatalog("kind", "Game.Kind") }, "M:Target.Echo(Game.Kind)")
        });
        var lua = new ScriptApiCatalog(new[] { target }, enums: entries).ToLuaDefinitions();
        Assert.Contains("---@alias Enum_Game_002e_Kind\n---| \"None\"", lua);
        Assert.DoesNotContain("---@enum", lua);
        Assert.Contains("---@field Kind Enum_Game_002e_Kind # Read-only.", lua);
        Assert.Contains("---@field Flags Enum_Game_002e_Flags|string # Read-only.", lua);
        Assert.Contains("---@param kind string", lua);
        Assert.Contains("---@return Enum_Game_002e_Kind", lua);
    }

    [Fact]
    public void Snapshot_exports_stay_within_Luas_active_local_limit()
    {
        var types = Enumerable.Range(0, 170).Select(index => new ScriptTypeCatalog("Target" + index,
            "Game.Target" + index, Array.Empty<ScriptMemberCatalog>())).ToArray();
        var events = types.Select((type, index) => new ScriptEventCatalogEntry("Test", "Event" + index,
            "Test.Events", "Event" + index, new[] { new ScriptParameterCatalog("args", type.ClrType) }));
        new Script(CoreModules.None).LoadString(new ScriptApiCatalog(types, events).ToLuaDefinitions());
    }

    [Fact]
    public void Nullable_event_value_types_declare_their_underlying_snapshot()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<SnapshotValue>();
        var entry = new ScriptEventCatalogEntry("Test", "Value", "Test.Events", "Value",
            new[] { new ScriptParameterCatalog("value", typeof(SnapshotValue?).FullName!) });
        var lua = registry.GetCatalog().WithEvents(new[] { entry }).ToLuaDefinitions();
        Assert.Contains("---@class EventSnapshot_SnapshotValue", lua);
        Assert.Contains("fun(value: EventSnapshot_SnapshotValue|nil)", lua);
    }

    [ScriptType]
    public sealed class SnapshotArgs
    {
        [ScriptVisible] public SnapshotTarget Target => new();
    }

    [ScriptType]
    public sealed class SnapshotTarget
    {
        public bool Destroyed;
        [ScriptVisible] public bool IsDestroyed => Destroyed;
        [ScriptVisible] public int Id => Destroyed ? throw new InvalidOperationException("Dead getter") : 1;
        [ScriptVisible] public bool Enabled => Destroyed ? throw new InvalidOperationException("Dead getter") : true;
    }

    [ScriptType]
    public struct SnapshotValue
    {
        [ScriptVisible] public int Value => 1;
    }
}
