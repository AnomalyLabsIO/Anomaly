using Il2CppMirror;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anomaly.Client.Assets
{
    /// <summary>
    /// Resolves <c>(netId, componentPath, materialIndex)</c> → <see cref="Material"/>
    /// on the client and applies / reverts texture overrides. Pure utility class:
    /// no state of its own; all state lives in <see cref="OverrideRegistry"/>.
    /// </summary>
    internal static class OverrideApplier
    {
        // SCP:SL's HDRP and character shaders do not consistently expose
        // Unity's legacy _MainTex property. Prefer the shader's declaration,
        // then the supported pipelines' base-color texture names.
        private static bool TryGetTextureProperty(Material material, out int propertyId)
        {
            var shader = material.shader;
            if (shader != null)
            {
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainTexture) == 0) continue;
                    propertyId = shader.GetPropertyNameId(i);
                    return true;
                }
            }
            foreach (string name in new[] { "_BaseColorMap", "_BaseMap", "_MainTex" })
            {
                propertyId = Shader.PropertyToID(name);
                if (material.HasProperty(propertyId)) return true;
            }
            propertyId = 0;
            return false;
        }

        /// <summary>
        /// Resolves the target material. Returns null if the NetworkIdentity is
        /// not spawned on this client, or the component path does not point at a
        /// <see cref="Renderer"/>, or the material index is out of range.
        /// </summary>
        public static Material ResolveMaterial(uint netId, string componentPath, int materialIndex)
        {
            if (!NetworkClient.spawned.TryGetValue(netId, out var ni) || ni == null || ni.gameObject == null)
                return null;

            Renderer renderer = null;
            if (string.IsNullOrEmpty(componentPath))
            {
                renderer = ni.gameObject.GetComponentInChildren<Renderer>(true);
            }
            else
            {
                var t = ni.transform.Find(componentPath);
                if (t == null) return null;
                renderer = t.GetComponent<Renderer>();
            }

            if (renderer == null) return null;

            // .materials clones; we want the live material references to persist
            // between calls (so originals can be restored), so use sharedMaterials
            // for lookup and .materials to fetch the live-instanced array.
            var mats = renderer.materials;
            if (materialIndex < 0 || materialIndex >= mats.Length) return null;
            return mats[materialIndex];
        }

        /// <summary>
        /// Applies <paramref name="newTexture"/> to the material and returns the
        /// previous selected base-texture property so it can be restored later. Also needs the
        /// <see cref="Renderer"/> so the mutated materials array can be written back.
        /// </summary>
        public static Texture ApplyTexture(uint netId, string componentPath, int materialIndex, Texture newTexture, out Material material, out int texturePropertyId)
        {
            material = null;
            texturePropertyId = 0;
            if (!NetworkClient.spawned.TryGetValue(netId, out var ni) || ni == null || ni.gameObject == null)
                return null;

            Renderer renderer = ResolveRenderer(ni, componentPath);
            if (renderer == null) return null;

            var mats = renderer.materials;
            if (materialIndex < 0 || materialIndex >= mats.Length) return null;

            var target = mats[materialIndex];
            if (target == null || !TryGetTextureProperty(target, out texturePropertyId)) return null;
            material = target;
            var original = material.GetTexture(texturePropertyId);
            bool textureChanged = false;
            try
            {
                // Set ownership before invoking Unity: a property setter may
                // mutate native state and still throw before returning.
                textureChanged = true;
                material.SetTexture(texturePropertyId, newTexture);
                renderer.materials = mats;
                return original;
            }
            catch
            {
                // Do not leave an untracked remote mutation behind when Unity
                // rejects the later renderer-array publication. OverrideRegistry
                // only records ownership after this method returns successfully.
                if (textureChanged)
                {
                    try { material.SetTexture(texturePropertyId, original); }
                    catch { }
                    try { renderer.materials = mats; }
                    catch { }
                }
                material = null;
                throw;
            }
        }

        /// <summary>
        /// Restores <paramref name="originalTexture"/> onto the material slot.
        /// Silently no-ops if the target is gone (object despawned, renderer
        /// destroyed).
        /// </summary>
        public static void RevertTexture(uint netId, string componentPath, int materialIndex, Texture originalTexture)
        {
            if (!NetworkClient.spawned.TryGetValue(netId, out var ni) || ni == null || ni.gameObject == null)
                return;

            var renderer = ResolveRenderer(ni, componentPath);
            if (renderer == null) return;

            var mats = renderer.materials;
            if (materialIndex < 0 || materialIndex >= mats.Length) return;

            if (!TryGetTextureProperty(mats[materialIndex], out int propertyId)) return;
            mats[materialIndex].SetTexture(propertyId, originalTexture);
            renderer.materials = mats;
        }

        private static Renderer ResolveRenderer(NetworkIdentity ni, string componentPath)
        {
            if (string.IsNullOrEmpty(componentPath))
                return ni.gameObject.GetComponentInChildren<Renderer>(true);

            var t = ni.transform.Find(componentPath);
            return t != null ? t.GetComponent<Renderer>() : null;
        }
    }
}
