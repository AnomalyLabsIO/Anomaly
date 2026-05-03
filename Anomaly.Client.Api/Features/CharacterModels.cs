using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Scheduling;
using Il2CppInterop.Runtime;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Helpers for working with SCP:SL character models — animator hook
/// registration, layer-index lookup, secondary-rig sync, and look-at
/// suppression. Every helper is null-safe and takes an
/// <see cref="AnimatedCharacterModel"/> directly so mods can target either
/// the local model or specific remote models.
/// </summary>
/// <remarks>
/// <para><b>Animator hook lifecycle.</b> SCP:SL advances the animator from
/// <c>CullingSubcontroller.LateUpdate</c> via per-instance
/// <c>OnBeforeAnimatorUpdated</c> and <c>OnAnimatorUpdated</c> events.
/// <see cref="RegisterLocalHook"/> auto-binds an <see cref="IModelHook"/> to
/// the local player's current model and re-binds whenever
/// <see cref="RoleEvents.Changed"/> fires for the local player. This solves
/// the "model gets pooled on role change" problem without each mod tracking
/// model lifecycle manually.</para>
///
/// <para><b>Why these helpers exist.</b> Mods that drive character bones
/// directly from <see cref="UnityEngine.MonoBehaviour"/> <c>LateUpdate</c>
/// either get clobbered by the next animator pass (writes too early) or
/// fight competing solvers (<see cref="LookatSubcontroller"/> in particular).
/// The helpers in this class encode the eight pieces of game-internal
/// knowledge documented in MODDING.md's "Character model" section.</para>
/// </remarks>
public static class CharacterModels
{
    private sealed class HookEntry
    {
        public string Id;
        public IModelHook Hook;
        public AnimatedCharacterModel AttachedModel;
        public Action BeforeDelegate;
        public Action AfterDelegate;
        public ScheduledTask RetryTask;
    }

    private const int LocalAttachRetryCount = 20;
    private static readonly TimeSpan LocalAttachRetryDelay = TimeSpan.FromMilliseconds(100);

    private static readonly List<HookEntry> _localHooks = new();
    private static readonly Dictionary<int, int> _animatorLayerCache = new();
    private static bool _hookInstalled;

    /// <summary>
    /// The local player's current <see cref="AnimatedCharacterModel"/>, or
    /// null if the player has no FPC role / is dead / spectating.
    /// </summary>
    public static AnimatedCharacterModel LocalModel
    {
        get
        {
            LocalPlayer local = LocalPlayer.Instance;
            if (local == null) return null;
            return local.CharacterModel as AnimatedCharacterModel;
        }
    }

    /// <summary>
    /// Registers <paramref name="hook"/> against the local player's current
    /// model. The framework auto-attaches and re-attaches across role
    /// changes — mods don't need to track model lifecycle. Returns an
    /// <see cref="IDisposable"/> that unregisters on dispose.
    /// </summary>
    /// <param name="hook">Hook implementation. Will be invoked from the engine's animator timing.</param>
    public static IDisposable RegisterLocalHook(IModelHook hook)
    {
        if (hook == null) throw new ArgumentNullException(nameof(hook));

        EnsureRoleHook();

        HookEntry entry = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Hook = hook,
        };
        _localHooks.Add(entry);

        AttachToCurrentLocalModel(entry);
        return new HookHandle(entry);
    }

    /// <summary>
    /// Cached <c>Animator.GetLayerIndex(name)</c>. Avoids the per-call string
    /// hash that vanilla Unity does. Returns <c>-1</c> if the layer doesn't exist.
    /// </summary>
    public static int GetAnimatorLayerIndex(Animator animator, string layerName)
    {
        if (animator == null || string.IsNullOrEmpty(layerName))
            return -1;

        int key = animator.GetInstanceID() ^ layerName.GetHashCode();
        if (_animatorLayerCache.TryGetValue(key, out int cached))
            return cached;

        int idx = animator.GetLayerIndex(layerName);
        _animatorLayerCache[key] = idx;
        return idx;
    }

    /// <summary>
    /// Calls <c>SecondaryRigsSubcontroller.MatchAll()</c> on
    /// <paramref name="model"/>'s secondary rigs subcontroller, syncing
    /// mirrored / helper bones to whatever bone state the caller just wrote.
    /// Mods that override bones in <see cref="IModelHook.OnAnimatorUpdated"/>
    /// should call this afterward to prevent asymmetric flicker.
    /// </summary>
    public static bool SyncSecondaryRigs(AnimatedCharacterModel model)
    {
        if (model == null) return false;

        try
        {
            if (!model.TryGetSubcontroller<SecondaryRigsSubcontroller>(out SecondaryRigsSubcontroller sub) || sub == null)
                return false;
            sub.MatchAll();
            return true;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"CharacterModels.SyncSecondaryRigs threw: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Enables or disables the model's <see cref="LookatSubcontroller"/>.
    /// Mods that drive head/torso directly should suppress to prevent the
    /// native solver from adding competing chest/neck motion.
    /// </summary>
    public static bool SuppressLookAt(AnimatedCharacterModel model, bool suppress)
    {
        if (model == null) return false;

        try
        {
            if (!model.TryGetSubcontroller<LookatSubcontroller>(out LookatSubcontroller sub) || sub == null)
                return false;
            sub.enabled = !suppress;
            return true;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"CharacterModels.SuppressLookAt threw: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns the model's <see cref="CullingSubcontroller"/> — the component
    /// that drives the animator update cycle. Useful for mods that need
    /// direct access (e.g. to read <c>SkipAnimationUpdate</c> for adaptive
    /// behavior). Returns null if the subcontroller isn't present or hasn't
    /// initialized yet.
    /// </summary>
    public static CullingSubcontroller GetCullingSubcontroller(AnimatedCharacterModel model)
    {
        if (model == null) return null;

        try
        {
            return model.TryGetSubcontroller<CullingSubcontroller>(out CullingSubcontroller sub) ? sub : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Called by the loader on disconnect to drop hook subscriptions.</summary>
    internal static void Reset()
    {
        for (int i = _localHooks.Count - 1; i >= 0; i--)
            DetachHook(_localHooks[i]);
        _localHooks.Clear();
        _animatorLayerCache.Clear();
    }

    private static void EnsureRoleHook()
    {
        if (_hookInstalled) return;
        _hookInstalled = true;
        RoleEvents.Changed += OnAnyRoleChanged;
    }

    private static void OnAnyRoleChanged(PlayerRoleChangedEventArgs args)
    {
        if (args == null || args.Player == null || !args.Player.IsLocal)
            return;

        for (int i = 0; i < _localHooks.Count; i++)
        {
            DetachHook(_localHooks[i]);
            AttachToCurrentLocalModel(_localHooks[i]);
        }
    }

    private static void AttachToCurrentLocalModel(HookEntry entry)
    {
        AttachToCurrentLocalModel(entry, LocalAttachRetryCount);
    }

    private static void AttachToCurrentLocalModel(HookEntry entry, int retriesLeft)
    {
        if (entry == null || entry.Hook == null)
            return;

        entry.RetryTask?.Cancel();
        entry.RetryTask = null;

        AnimatedCharacterModel model = LocalModel;
        if (model == null)
        {
            ScheduleAttachRetry(entry, retriesLeft);
            return;
        }

        CullingSubcontroller sub = GetCullingSubcontroller(model);
        if (sub == null)
        {
            ScheduleAttachRetry(entry, retriesLeft);
            return;
        }

        try
        {
            entry.AttachedModel = model;

            entry.BeforeDelegate = () => SafeInvoke(entry.Hook, model, before: true);
            entry.AfterDelegate = () => SafeInvoke(entry.Hook, model, before: false);

            sub.OnBeforeAnimatorUpdated += DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(entry.BeforeDelegate);
            sub.OnAnimatorUpdated += DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(entry.AfterDelegate);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"CharacterModels.AttachToCurrentLocalModel threw: {ex.Message}");
            entry.AttachedModel = null;
            entry.BeforeDelegate = null;
            entry.AfterDelegate = null;
        }
    }

    private static void ScheduleAttachRetry(HookEntry entry, int retriesLeft)
    {
        if (entry == null || retriesLeft <= 0)
            return;

        entry.RetryTask = Scheduler.In(LocalAttachRetryDelay, () =>
        {
            if (_localHooks.Contains(entry) && entry.AttachedModel == null)
                AttachToCurrentLocalModel(entry, retriesLeft - 1);
        }, ownerId: "anomaly.character-models");
    }

    private static void DetachHook(HookEntry entry)
    {
        if (entry == null) return;

        entry.RetryTask?.Cancel();
        entry.RetryTask = null;

        if (entry.AttachedModel == null) return;

        CullingSubcontroller sub = GetCullingSubcontroller(entry.AttachedModel);
        if (sub != null)
        {
            try
            {
                if (entry.BeforeDelegate != null)
                    sub.OnBeforeAnimatorUpdated -= DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(entry.BeforeDelegate);
                if (entry.AfterDelegate != null)
                    sub.OnAnimatorUpdated -= DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(entry.AfterDelegate);
            }
            catch { /* best-effort detach */ }
        }

        entry.AttachedModel = null;
        entry.BeforeDelegate = null;
        entry.AfterDelegate = null;
    }

    private static void SafeInvoke(IModelHook hook, AnimatedCharacterModel model, bool before)
    {
        try
        {
            if (before) hook.OnBeforeAnimatorUpdated(model);
            else hook.OnAnimatorUpdated(model);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"IModelHook.{(before ? "OnBeforeAnimatorUpdated" : "OnAnimatorUpdated")} threw: {ex}");
        }
    }

    private sealed class HookHandle : IDisposable
    {
        private HookEntry _entry;
        public HookHandle(HookEntry entry) { _entry = entry; }
        public void Dispose()
        {
            if (_entry == null) return;
            DetachHook(_entry);
            _localHooks.Remove(_entry);
            _entry = null;
        }
    }
}
