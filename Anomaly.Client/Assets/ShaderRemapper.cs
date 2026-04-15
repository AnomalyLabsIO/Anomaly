using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Assets
{
    /// <summary>
    /// SCP:SL renders with HDRP. Asset bundles built outside the game's Unity
    /// project frequently reference shaders that aren't in the player's build, or
    /// reference them through a Shader asset reference that didn't serialize
    /// correctly. This helper walks all materials on an instantiated GameObject
    /// and tries to resolve any broken shaders via <c>Shader.Find</c>, falling
    /// back to a magenta unlit material so failures are visually obvious.
    /// </summary>
    public static class ShaderRemapper
    {
        private const string FallbackShaderName = "HDRP/Unlit";
        private const string DefaultLitShader   = "HDRP/Lit";

        private static Shader _fallbackShader;
        private static Material _fallbackMaterial;
        private static readonly HashSet<string> _loggedMissing = new();

        public static void Remap(GameObject root)
        {
            if (root == null) return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                if (mats == null) continue;

                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null) continue;

                    var shader = mat.shader;
                    if (shader != null && shader.isSupported)
                        continue;

                    // Try to re-resolve by name in the running game's shader set.
                    string wantedName = shader != null ? shader.name : DefaultLitShader;
                    var resolved = Shader.Find(wantedName);
                    if (resolved != null && resolved.isSupported)
                    {
                        mat.shader = resolved;
                        changed = true;
                        continue;
                    }

                    // Last resort: magenta fallback.
                    if (_loggedMissing.Add(wantedName))
                        MelonLogger.Warning($"ShaderRemapper: could not resolve shader '{wantedName}'; using fallback.");

                    mats[i] = GetFallbackMaterial();
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = mats;
            }
        }

        private static Material GetFallbackMaterial()
        {
            if (_fallbackMaterial != null) return _fallbackMaterial;

            _fallbackShader = Shader.Find(FallbackShaderName) ?? Shader.Find(DefaultLitShader);
            _fallbackMaterial = new Material(_fallbackShader)
            {
                color = Color.magenta
            };
            _fallbackMaterial.hideFlags = HideFlags.HideAndDontSave;
            return _fallbackMaterial;
        }
    }
}
