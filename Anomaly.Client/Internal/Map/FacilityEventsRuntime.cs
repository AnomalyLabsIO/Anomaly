using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal;
using HarmonyLib;
using Il2CppHazards;
using Il2CppInteractables.Interobjects.DoorUtils;
using Il2CppInterop.Runtime;
using Il2CppPlayerRoles.PlayableScps.Scp939;
using MelonLoader;
using UnityEngine;
using DoorEvents = Anomaly.Client.Api.Events.Handlers.DoorEvents;

namespace Anomaly.Client.Internal.Map;

/// <summary>Owns native facility event subscriptions independently of mod subscribers.</summary>
internal static class FacilityEventsRuntime
{
    private static readonly TransactionalInitializer Initializer = new();
    private static readonly Dictionary<DoorVariant, DoorSubscription> Doors = new();
    private static readonly HashSet<EnvironmentalHazard> Hazards = new();
    private static float _nextPrune;

    internal static void Init()
    {
        if (Initializer.IsInitialized)
            return;

        var doorAdded = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<DoorVariant>>(
            new Action<DoorVariant>(HandleDoorAdded));
        var doorRemoved = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<DoorVariant>>(
            new Action<DoorVariant>(HandleDoorRemoved));
        var hazardAdded = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<EnvironmentalHazard>>(
            new Action<EnvironmentalHazard>(HandleHazardAdded));
        var hazardRemoved = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<EnvironmentalHazard>>(
            new Action<EnvironmentalHazard>(HandleHazardRemoved));

        Initializer.Initialize(
            new InitializationStep(() => DoorVariant.OnInstanceCreated += doorAdded, () => DoorVariant.OnInstanceCreated -= doorAdded),
            new InitializationStep(() => DoorVariant.OnInstanceRemoved += doorRemoved, () => DoorVariant.OnInstanceRemoved -= doorRemoved),
            new InitializationStep(() => EnvironmentalHazard.OnAdded += hazardAdded, () => EnvironmentalHazard.OnAdded -= hazardAdded),
            new InitializationStep(() => EnvironmentalHazard.OnRemoved += hazardRemoved, () => EnvironmentalHazard.OnRemoved -= hazardRemoved),
            new InitializationStep(Resync, Clear));
    }

    /// <summary>Reattaches after scene-cache clearing; existing objects do not replay Added events.</summary>
    internal static void Resync()
    {
        if (DoorVariant.AllDoors != null)
            foreach (DoorVariant door in DoorVariant.AllDoors)
                AddDoor(door, false);

        // One scene-boundary scan, never per-frame discovery.
        foreach (EnvironmentalHazard hazard in UnityEngine.Object.FindObjectsOfType<EnvironmentalHazard>())
            AddHazard(hazard, false);
    }

    internal static void Shutdown()
    {
        Initializer.Reset();
        Clear();
    }

    internal static void Clear()
    {
        foreach (DoorSubscription subscription in Doors.Values)
            subscription.Dispose();
        Doors.Clear();
        Hazards.Clear();
        Door.ClearAll();
        Hazard.ClearAll();
        Workstation.ClearAll();
        LightsController.ClearAll();
        Window.ClearAll();
        Intercom.ClearAll();
        _nextPrune = 0f;
    }

    internal static void Tick()
    {
        if (!Initializer.IsInitialized || Time.unscaledTime < _nextPrune)
            return;
        _nextPrune = Time.unscaledTime + 1f;

        // Native removal events normally evict immediately. This only prunes
        // destroyed cached objects, covering teardown without a scene-wide scan.
        foreach (DoorVariant door in Doors.Keys.Where(value => value == null).ToArray())
            RemoveDoor(door, false);
        foreach (EnvironmentalHazard hazard in Hazards.Where(value => value == null).ToArray())
            RemoveHazard(hazard, false);
        Hazard.RemoveDestroyed();
        Workstation.RemoveDestroyed();
        LightsController.RemoveDestroyed();
        Window.RemoveDestroyed();
        Intercom.RemoveDestroyed();
        Locker.RemoveDestroyed();
        Generator.RemoveDestroyed();
    }

    private static void AddDoor(DoorVariant value, bool publish)
    {
        if (value == null || Doors.ContainsKey(value))
            return;
        Door wrapper = Door.Get(value);
        DoorSubscription subscription = new(value, wrapper);
        subscription.Attach();
        Doors[value] = subscription;
        if (publish)
            DoorEvents.OnAdded(new DoorEventArgs(wrapper));
    }

    private static void RemoveDoor(DoorVariant value, bool publish)
    {
        if (ReferenceEquals(value, null))
            return;
        Door wrapper = null;
        bool wasTracked = Doors.Remove(value, out DoorSubscription subscription);
        if (wasTracked)
        {
            wrapper = subscription.Wrapper;
            subscription.Dispose();
        }
        if (Door.Unregister(value, out Door cached))
            wrapper ??= cached;
        if (publish && wasTracked && wrapper != null)
            DoorEvents.OnRemoved(new DoorEventArgs(wrapper));
    }

    private static void HandleDoorAdded(DoorVariant value)
    {
        if (Initializer.IsInitialized)
            Safely(() => AddDoor(value, true), "door added");
    }

    private static void HandleDoorRemoved(DoorVariant value)
    {
        if (Initializer.IsInitialized)
            Safely(() => RemoveDoor(value, true), "door removed");
    }

    private static void AddHazard(EnvironmentalHazard value, bool publish)
    {
        if (value == null || !Hazards.Add(value))
            return;
        Hazard wrapper = Hazard.Get(value);
        if (publish && wrapper != null)
            HazardEvents.OnAdded(new HazardEventArgs(wrapper));
    }

    private static void RemoveHazard(EnvironmentalHazard value, bool publish)
    {
        if (ReferenceEquals(value, null))
            return;
        bool wasTracked = Hazards.Remove(value);
        if (Hazard.Unregister(value, out Hazard wrapper) && publish && wasTracked)
            HazardEvents.OnRemoved(new HazardEventArgs(wrapper));
    }

    private static void HandleHazardAdded(EnvironmentalHazard value)
    {
        if (Initializer.IsInitialized)
            Safely(() => AddHazard(value, true), "hazard added");
    }

    private static void HandleHazardRemoved(EnvironmentalHazard value)
    {
        if (Initializer.IsInitialized)
            Safely(() => RemoveHazard(value, true), "hazard removed");
    }

    private static void Safely(Action action, string context)
    {
        try { action(); }
        catch (Exception ex) { MelonLogger.Error($"FacilityEventsRuntime ({context}): {ex}"); }
    }

    // The game's OnDestroy returns before OnInstanceRemoved when Rooms has
    // not been registered yet. Publish while the component is still valid;
    // the tracked subscription prevents the later native callback repeating it.
    [HarmonyPatch(typeof(DoorVariant), nameof(DoorVariant.OnDestroy))]
    private static class DoorDestroyPatch
    {
        [HarmonyPrefix]
        private static void Prefix(DoorVariant __instance) => HandleDoorRemoved(__instance);
    }

    // The native EnvironmentalHazard callbacks are raised only while
    // NetworkServer.active. Unity lifecycle hooks also cover ordinary clients.
    // The tracked set deduplicates base/override hooks and host-native callbacks.
    [HarmonyPatch(typeof(EnvironmentalHazard), nameof(EnvironmentalHazard.Awake))]
    private static class HazardAwakePatch
    {
        [HarmonyPostfix]
        private static void Postfix(EnvironmentalHazard __instance) => HandleHazardAdded(__instance);
    }

    [HarmonyPatch]
    private static class HazardDestroyPatch
    {
        [HarmonyTargetMethods]
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(EnvironmentalHazard), nameof(EnvironmentalHazard.OnDestroy));
            yield return AccessTools.DeclaredMethod(typeof(TantrumEnvironmentalHazard), nameof(TantrumEnvironmentalHazard.OnDestroy));
            yield return AccessTools.DeclaredMethod(typeof(Scp939AmnesticCloudInstance), nameof(Scp939AmnesticCloudInstance.OnDestroy));
        }

        // Read the wrapper before the native component and subclass state are
        // torn down. TemporaryHazard and Sinkhole use the base implementation.
        [HarmonyPrefix]
        private static void Prefix(EnvironmentalHazard __instance) => HandleHazardRemoved(__instance);
    }

    private sealed class DoorSubscription : IDisposable
    {
        private readonly DoorVariant _base;
        private readonly Il2CppSystem.Action _stateChanged;
        private readonly Il2CppSystem.Action _locksChanged;
        private bool _isOpen;
        private ushort _locks;

        internal DoorSubscription(DoorVariant value, Door wrapper)
        {
            _base = value;
            Wrapper = wrapper;
            _isOpen = value.TargetState;
            _locks = value.ActiveLocks;
            _stateChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => Safely(StateChanged, "door state")));
            _locksChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => Safely(LocksChanged, "door locks")));
        }

        internal Door Wrapper { get; }

        internal void Attach()
        {
            try
            {
                _base.add_OnStateChanged(_stateChanged);
                _base.add_OnLockChanged(_locksChanged);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_base == null)
                return;
            try { _base.remove_OnStateChanged(_stateChanged); }
            catch (Exception ex) { MelonLogger.Warning($"Could not detach door state callback: {ex.Message}"); }
            try { _base.remove_OnLockChanged(_locksChanged); }
            catch (Exception ex) { MelonLogger.Warning($"Could not detach door lock callback: {ex.Message}"); }
        }

        private void StateChanged()
        {
            if (_base == null)
                return;
            bool current = _base.TargetState;
            bool previous = _isOpen;
            _isOpen = current;
            if (previous != current)
                DoorEvents.OnStateChanged(new DoorStateChangedEventArgs(Wrapper, previous, current));
        }

        private void LocksChanged()
        {
            if (_base == null)
                return;
            ushort current = _base.ActiveLocks;
            ushort previous = _locks;
            _locks = current;
            if (previous != current)
                DoorEvents.OnLocksChanged(new DoorLocksChangedEventArgs(Wrapper, previous, current));
        }
    }
}
