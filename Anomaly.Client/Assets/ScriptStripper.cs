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
