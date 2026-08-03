using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Assets
{
    /// <summary>
    /// Destroys every <see cref="MonoBehaviour"/> on a freshly-instantiated
    /// bundle asset. The v1 asset pipeline disallows script execution from
    /// server-shipped bundles — behavior is expressed exclusively through
    /// spawn/modify/despawn messages, so any MonoBehaviour in a bundle is either
    /// accidentally included or attempted code-injection.
    /// </summary>
    /// <remarks>
    /// <see cref="Transform"/>, <see cref="MeshRenderer"/>, <see cref="MeshFilter"/>,
    /// <see cref="SkinnedMeshRenderer"/>, and <see cref="Animator"/> are not
    /// <c>MonoBehaviour</c> subclasses, so they survive and the visual prefab
    /// continues to render and animate as intended.
    /// </remarks>
    public static class ScriptStripper
    {
        /// <summary>
        /// Performs a fail-closed check on the prefab asset before Instantiate can run any
        /// component lifecycle callbacks. Destruction after Instantiate is too late for Awake.
        /// </summary>
        public static bool IsSafePrefab(GameObject root, out string reason)
        {
            if (root == null)
            {
                reason = "prefab was null";
                return false;
            }

            var components = root.GetComponentsInChildren<MonoBehaviour>(true);
            if (components.Length == 0)
            {
                reason = null;
                return true;
            }

            var types = new HashSet<string>();
            foreach (var component in components)
            {
                if (component == null)
                    continue;
                types.Add(component.GetType().FullName ?? component.GetType().Name);
            }

            reason = types.Count == 0
                ? "prefab contained an unresolved MonoBehaviour"
                : $"prefab contained MonoBehaviour components ({string.Join(", ", types)})";
            return false;
        }

        public static void Strip(GameObject root)
        {
            if (root == null) return;

            var stripped = new HashSet<string>();
            var components = root.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (var mb in components)
            {
                if (mb == null) continue;
                stripped.Add(mb.GetType().FullName ?? mb.GetType().Name);
                UnityEngine.Object.Destroy(mb);
            }

            if (stripped.Count > 0)
                MelonLogger.Warning(
                    $"ScriptStripper: stripped {components.Length} MonoBehaviour component(s) from '{root.name}': {string.Join(", ", stripped)}");
        }
    }
}
