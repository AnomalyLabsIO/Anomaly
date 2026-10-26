using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Events.Handlers;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Cached resolver for the local game camera with multi-fallback resolution.
/// Replaces the brittle <c>GameObject.Find("Main Camera Controller/...")</c>
/// path that <see cref="LocalPlayer.Camera"/> previously used. Mods that need
/// a reliable camera reference (third-person cam, free-cam, photo mode,
/// VR rigs, custom render pipelines) should consume <see cref="Main"/> rather
/// than walking the scene themselves.
/// </summary>
/// <remarks>
/// <para><b>Resolution chain</b> (first non-null wins):</para>
/// <list type="number">
///   <item><description>Cached camera, if still usable.</description></item>
///   <item><description><see cref="MainCameraController.CurrentCamera"/> if the controller is initialized.</description></item>
///   <item><description>User-registered fallbacks (mod resolvers added via <see cref="RegisterFallback"/>), in registration order.</description></item>
///   <item><description><see cref="UnityEngine.Camera.main"/>.</description></item>
///   <item><description>Active scene walk (root GameObjects → all child cameras).</description></item>
/// </list>
/// <para>The cache invalidates automatically on (a) role change of the local
/// player, (b) scene load, and (c) when the cached <see cref="UnityEngine.Camera"/>
/// reference becomes null. Mods that swap the camera rig themselves should
/// call <see cref="Invalidate"/> to force re-resolution and fire
/// <see cref="Changed"/>.</para>
/// </remarks>
public static class CameraManager
{
    private sealed class FallbackEntry
    {
        public string ModId;
        public Func<Camera> Resolver;
    }

    private static readonly List<FallbackEntry> _fallbacks = new();
    private static Camera _cached;
    private static bool _hooksInstalled;

    /// <summary>
    /// Fires when <see cref="Main"/> resolves to a different
    /// <see cref="UnityEngine.Camera"/> instance than the previously-cached one.
    /// Useful for mods that need to re-attach VFX, UI, or post-process bindings
    /// when the rig swaps (role change, scene change, custom override).
    /// </summary>
    [ScriptIgnore]
    public static event Action<Camera> Changed;

    /// <summary>
    /// The current best-known game camera. Cached; cheap to call in hot loops.
    /// Returns null if no camera can be resolved (rare — pre-bootstrap or
    /// during scene transitions).
    /// </summary>
    public static Camera Main
    {
        get
        {
            EnsureHooksInstalled();

            if (IsUsable(_cached))
                return _cached;

            Camera resolved = Resolve();
            SetCached(resolved);
            return resolved;
        }
    }

    /// <summary>
    /// Drops the cached camera reference. Mods rarely need this — the cache
    /// invalidates itself on role/scene change. Use when you've swapped the
    /// camera rig in a way the framework can't detect.
    /// </summary>
    public static void Invalidate()
    {
        _cached = null;
    }

    /// <summary>
    /// Registers a mod-supplied camera resolver. Resolvers run in registration
    /// order between <see cref="MainCameraController.CurrentCamera"/> and
    /// <see cref="UnityEngine.Camera.main"/>. Returns an <see cref="IDisposable"/>
    /// handle that removes the resolver on dispose.
    /// </summary>
    /// <param name="modId">Mod-namespaced id (used for diagnostics/duplicate detection).</param>
    /// <param name="resolver">Returns a camera or null if the resolver doesn't apply right now.</param>
    public static IDisposable RegisterFallback(string modId, Func<Camera> resolver)
    {
        if (string.IsNullOrEmpty(modId))
            throw new ArgumentException("Mod id must be non-empty.", nameof(modId));
        if (resolver == null)
            throw new ArgumentNullException(nameof(resolver));

        FallbackEntry entry = new() { ModId = modId, Resolver = resolver };
        _fallbacks.Add(entry);
        Invalidate();
        return new FallbackHandle(entry);
    }

    /// <summary>Drops every registered fallback and clears the cache. Called by the loader on disconnect.</summary>
    internal static void Reset()
    {
        _fallbacks.Clear();
        _cached = null;
    }

    private static void EnsureHooksInstalled()
    {
        if (_hooksInstalled)
            return;
        _hooksInstalled = true;

        try { RoleEvents.Changed += OnAnyRoleChanged; }
        catch (Exception ex) { MelonLogger.Warning($"CameraManager: failed to hook RoleEvents.Changed: {ex.Message}"); }

        try { SceneManager.add_sceneLoaded(new Action<Scene, LoadSceneMode>(OnSceneLoaded)); }
        catch (Exception ex) { MelonLogger.Warning($"CameraManager: failed to hook sceneLoaded: {ex.Message}"); }
    }

    private static void OnAnyRoleChanged(Anomaly.Client.Api.Events.Args.PlayerRoleChangedEventArgs args)
    {
        if (args == null || args.Player == null || !args.Player.IsLocal)
            return;
        Invalidate();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Invalidate();

    private static Camera Resolve()
    {
        try
        {
            if (MainCameraController.InstanceActive && MainCameraController.CurrentCamera != null)
            {
                Camera c = MainCameraController.CurrentCamera.GetComponent<Camera>();
                if (IsUsable(c))
                    return c;
            }
        }
        catch { /* fall through */ }

        for (int i = 0; i < _fallbacks.Count; i++)
        {
            try
            {
                Camera c = _fallbacks[i].Resolver?.Invoke();
                if (IsUsable(c))
                    return c;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CameraManager: fallback '{_fallbacks[i].ModId}' threw: {ex.Message}");
            }
        }

        try
        {
            Camera c = Camera.main;
            if (IsUsable(c))
                return c;
        }
        catch { /* fall through */ }

        try
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i] == null)
                        continue;
                    Camera[] found = roots[i].GetComponentsInChildren<Camera>(includeInactive: true);
                    for (int j = 0; j < found.Length; j++)
                        if (IsUsable(found[j]))
                            return found[j];
                }
            }
        }
        catch { /* fall through */ }

        return null;
    }

    private static void SetCached(Camera c)
    {
        if (ReferenceEquals(_cached, c))
            return;
        _cached = c;
        EventInvoker.Invoke(Changed, c, "CameraManager.Changed");
    }

    private static bool IsUsable(Camera c) => c != null && c.gameObject != null && c.enabled;

    private sealed class FallbackHandle : IDisposable
    {
        private FallbackEntry _entry;
        public FallbackHandle(FallbackEntry entry) { _entry = entry; }
        public void Dispose()
        {
            if (_entry == null) return;
            _fallbacks.Remove(_entry);
            CameraManager.Invalidate();
            _entry = null;
        }
    }
}
