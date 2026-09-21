using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Local-only manipulation of arbitrary GameObjects in the scene — typically
/// non-networked map decorations, lights, props, particles, decals, ambient
/// stuff. Setting a transform here only affects the calling client; for
/// multi-client synchronized motion, see the planned synced WorldObjects API
/// (Anomaly server message + matching client receiver).
/// </summary>
/// <remarks>
/// <para>The cache stores each object's <b>original local</b> position,
/// rotation, and scale on first override, so <see cref="ResetTransform"/>
/// always restores the level-designer-placed pose even if the parent has
/// moved in the meantime. Cache is lifecycle-cleaned on disconnect and
/// lazily evicts entries whose <see cref="GameObject"/> has been destroyed.</para>
///
/// <para><b>Caveats — read these before shipping a mod that uses this:</b></para>
/// <list type="bullet">
///   <item><description><b>Static lighting won't update.</b> Anything baked
///     into the lightmap stays in its original position visually under the
///     object — affects shadows and bounce light.</description></item>
///   <item><description><b>Static batching is invalidated.</b> Setting
///     <c>localScale</c> on a statically-batched object disables batching
///     for that object. Small perf cost; measurable for 100+ moved objects.</description></item>
///   <item><description><b>SCP:SL distance culling is aggressive.</b> Moved
///     objects can pop in/out unexpectedly when crossing the camera's
///     culling threshold relative to their original position.</description></item>
///   <item><description><b>Animator-driven objects fight back.</b> If the
///     object has a live <see cref="UnityEngine.Animator"/>, your transform
///     writes will be overwritten on the next animator update. Either
///     disable the animator or write inside a <see cref="IModelHook"/>.</description></item>
///   <item><description><b>Don't use this on networked objects.</b> Pickups,
///     doors, players, ragdolls have replicated transforms — Mirror will
///     overwrite your local writes. Use the engine's own position/rotation
///     APIs on those.</description></item>
/// </list>
/// </remarks>
public static class WorldObjects
{
    private readonly struct OriginalTransform
    {
        public OriginalTransform(Vector3 localPos, Quaternion localRot, Vector3 localScale)
        {
            LocalPosition = localPos;
            LocalRotation = localRot;
            LocalScale = localScale;
        }
        public Vector3 LocalPosition { get; }
        public Quaternion LocalRotation { get; }
        public Vector3 LocalScale { get; }
    }

    private static readonly Dictionary<GameObject, OriginalTransform> _originals = new();

    // ----------------------------- World-space setters -----------------------------

    /// <summary>
    /// Sets the world-space position of <paramref name="obj"/>. Caches the
    /// original local pose on first call so <see cref="ResetTransform"/> can
    /// restore. Returns false if <paramref name="obj"/> is null or destroyed.
    /// </summary>
    public static bool SetPosition(GameObject obj, Vector3 worldPosition)
    {
        if (!EnsureCached(obj)) return false;
        obj.transform.position = worldPosition;
        return true;
    }

    /// <summary>
    /// Sets the world-space rotation of <paramref name="obj"/>. Caches the
    /// original local pose on first call.
    /// </summary>
    public static bool SetRotation(GameObject obj, Quaternion worldRotation)
    {
        if (!EnsureCached(obj)) return false;
        obj.transform.rotation = worldRotation;
        return true;
    }

    /// <summary>
    /// Sets the local scale of <paramref name="obj"/>. (Unity has no
    /// world-space scale — every Transform stores localScale.) Caches the
    /// original on first call. Note: writes to <c>localScale</c> invalidate
    /// static batching for this object.
    /// </summary>
    public static bool SetScale(GameObject obj, Vector3 scale)
    {
        if (!EnsureCached(obj)) return false;
        obj.transform.localScale = scale;
        return true;
    }

    /// <summary>Convenience: uniform scale.</summary>
    public static bool SetScale(GameObject obj, float uniformScale) =>
        SetScale(obj, new Vector3(uniformScale, uniformScale, uniformScale));

    /// <summary>
    /// Atomically sets all three components. Cheaper than three separate
    /// calls because the cache lookup happens once.
    /// </summary>
    public static bool SetTransform(GameObject obj, Vector3 worldPosition, Quaternion worldRotation, Vector3 scale)
    {
        if (!EnsureCached(obj)) return false;
        Transform t = obj.transform;
        t.position = worldPosition;
        t.rotation = worldRotation;
        t.localScale = scale;
        return true;
    }

    // ----------------------------- Local-space setters -----------------------------

    /// <summary>Sets the local-space position (relative to parent).</summary>
    public static bool SetLocalPosition(GameObject obj, Vector3 localPosition)
    {
        if (!EnsureCached(obj)) return false;
        obj.transform.localPosition = localPosition;
        return true;
    }

    /// <summary>Sets the local-space rotation (relative to parent).</summary>
    public static bool SetLocalRotation(GameObject obj, Quaternion localRotation)
    {
        if (!EnsureCached(obj)) return false;
        obj.transform.localRotation = localRotation;
        return true;
    }

    // ----------------------------- Relative motion -----------------------------

    /// <summary>Adds <paramref name="delta"/> to the world position.</summary>
    public static bool Translate(GameObject obj, Vector3 delta)
    {
        if (!EnsureCached(obj)) return false;
        obj.transform.position += delta;
        return true;
    }

    /// <summary>Multiplies the world rotation by <paramref name="delta"/>.</summary>
    public static bool Rotate(GameObject obj, Quaternion delta)
    {
        if (!EnsureCached(obj)) return false;
        obj.transform.rotation = delta * obj.transform.rotation;
        return true;
    }

    /// <summary>Rotates around <paramref name="axis"/> by <paramref name="angleDegrees"/> in world space.</summary>
    public static bool Rotate(GameObject obj, Vector3 axis, float angleDegrees)
    {
        if (!EnsureCached(obj)) return false;
        obj.transform.Rotate(axis, angleDegrees, Space.World);
        return true;
    }

    /// <summary>
    /// Orients the object so its forward vector points at <paramref name="target"/>.
    /// </summary>
    public static bool LookAt(GameObject obj, Vector3 target, Vector3? worldUp = null)
    {
        if (!EnsureCached(obj)) return false;
        if (worldUp.HasValue)
            obj.transform.LookAt(target, worldUp.Value);
        else
            obj.transform.LookAt(target);
        return true;
    }

    // ----------------------------- Reset -----------------------------

    /// <summary>
    /// Restores <paramref name="obj"/>'s local position, rotation, and scale
    /// to the values cached on the first override call. Removes the cache
    /// entry so the next override re-caches. Returns false if no override
    /// was ever applied or the object is destroyed.
    /// </summary>
    public static bool ResetTransform(GameObject obj)
    {
        if (obj == null) return false;
        if (!_originals.TryGetValue(obj, out OriginalTransform original)) return false;

        Transform t = obj.transform;
        t.localPosition = original.LocalPosition;
        t.localRotation = original.LocalRotation;
        t.localScale = original.LocalScale;
        _originals.Remove(obj);
        return true;
    }

    /// <summary>True if <paramref name="obj"/> has an active override.</summary>
    public static bool IsOverridden(GameObject obj) => obj != null && _originals.ContainsKey(obj);

    // ----------------------------- Find helpers -----------------------------

    /// <summary>
    /// Resolves a GameObject by hierarchy path under the active scene's
    /// roots. Path is <c>/</c>-separated, e.g.
    /// <c>"Lobby/Decorations/Bench (4)"</c>. Returns the first match or
    /// null. Includes inactive objects in the walk (unlike
    /// <see cref="GameObject.Find"/>).
    /// </summary>
    public static GameObject FindByPath(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath)) return null;

        string[] segments = scenePath.Split('/');
        if (segments.Length == 0) return null;

        Scene active = SceneManager.GetActiveScene();
        if (!active.IsValid()) return null;

        GameObject[] roots = active.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null || roots[i].name != segments[0]) continue;
            GameObject result = WalkPath(roots[i].transform, segments, 1);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>
    /// Wraps <see cref="GameObject.Find(string)"/> with a null-tolerant
    /// signature. Walks only active scene objects; for inactive objects use
    /// <see cref="FindByPath"/>.
    /// </summary>
    public static GameObject FindByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        try { return GameObject.Find(name); }
        catch { return null; }
    }

    /// <summary>Wraps <see cref="GameObject.FindGameObjectsWithTag(string)"/>.</summary>
    public static GameObject[] FindAllByTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return System.Array.Empty<GameObject>();
        try { return GameObject.FindGameObjectsWithTag(tag); }
        catch { return System.Array.Empty<GameObject>(); }
    }

    /// <summary>
    /// Wraps <see cref="UnityEngine.Object.FindObjectsOfType{T}()"/>.
    /// Returns every active component of <typeparamref name="T"/> in the
    /// scene; useful for discovering all instances of a particular game
    /// component (lights, particle systems, audio sources, etc.).
    /// </summary>
    public static T[] FindAllByComponent<T>() where T : Component
    {
        try { return UnityEngine.Object.FindObjectsOfType<T>(); }
        catch { return System.Array.Empty<T>(); }
    }

    // ----------------------------- Internals -----------------------------

    /// <summary>
    /// Caches <paramref name="obj"/>'s original local pose if not already
    /// cached. Returns false if the object is null or has been destroyed.
    /// </summary>
    private static bool EnsureCached(GameObject obj)
    {
        if (obj == null) return false;

        if (_originals.ContainsKey(obj))
            return true;

        try
        {
            Transform t = obj.transform;
            _originals[obj] = new OriginalTransform(t.localPosition, t.localRotation, t.localScale);
            return true;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Warning($"WorldObjects: failed to cache original transform: {ex.Message}");
            return false;
        }
    }

    private static GameObject WalkPath(Transform current, string[] segments, int index)
    {
        if (current == null || index >= segments.Length)
            return current?.gameObject;

        string segment = segments[index];
        for (int i = 0; i < current.childCount; i++)
        {
            Transform child = current.GetChild(i);
            if (child == null || child.name != segment) continue;
            GameObject result = WalkPath(child, segments, index + 1);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>
    /// Resets every overridden object to its cached original pose and clears
    /// the cache. Called by the loader on disconnect; mods rarely need this.
    /// Skips entries whose <see cref="GameObject"/> has been destroyed.
    /// </summary>
    internal static void Reset()
    {
        if (_originals.Count == 0) return;

        foreach (KeyValuePair<GameObject, OriginalTransform> kvp in _originals)
        {
            GameObject obj = kvp.Key;
            if (obj == null) continue;
            try
            {
                Transform t = obj.transform;
                t.localPosition = kvp.Value.LocalPosition;
                t.localRotation = kvp.Value.LocalRotation;
                t.localScale = kvp.Value.LocalScale;
            }
            catch { /* destroyed objects are fine to skip */ }
        }
        _originals.Clear();
    }

    /// <summary>
    /// Drops cache entries whose <see cref="GameObject"/> has been destroyed.
    /// Called by the loader's periodic sweep; mods don't need to call this.
    /// </summary>
    internal static int PruneDeadEntries()
    {
        if (_originals.Count == 0) return 0;

        List<GameObject> dead = null;
        foreach (GameObject obj in _originals.Keys)
        {
            if (obj == null)
            {
                dead ??= new List<GameObject>();
                dead.Add(obj);
            }
        }
        if (dead == null) return 0;
        foreach (GameObject obj in dead) _originals.Remove(obj);
        return dead.Count;
    }
}
