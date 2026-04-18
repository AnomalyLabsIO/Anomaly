using Anomaly.Shared.Networking.Messages;
using HarmonyLib;
using MapGeneration;
using UnityEngine;

namespace Anomaly.Server.Map.Patches
{
    /// <summary>
    /// Server-side mirrors of the client patches in
    /// <c>Anomaly.Client.Patches.Runtime.AtlasZoneGeneratorPatch</c>. Both
    /// sides apply the same recipe during <c>AtlasZoneGenerator.Generate</c>
    /// so they produce byte-identical facilities.
    /// </summary>
    [HarmonyPatch(typeof(AtlasZoneGenerator), nameof(AtlasZoneGenerator.GetPositionAndRotation))]
    internal static class AtlasZoneGeneratorGetPositionAndRotationPatch
    {
        private static void Postfix(AtlasZoneGenerator __instance, AtlasInterpretation toSpawn,
                                    ref Vector3 worldPosition, ref float yRotation)
        {
            if (!FacilityLayout.TryGet(__instance.TargetZone, toSpawn.Coords, out var entry)) return;
            if (entry.OpType != OpType.TransformOverride) return;

            if ((entry.DirtyMask & FacilityLayoutRecipeMessage.DirtyPosition) != 0)
                worldPosition += entry.Offset;

            if ((entry.DirtyMask & FacilityLayoutRecipeMessage.DirtyRotation) != 0)
                yRotation = entry.RotationY;
        }
    }

    [HarmonyPatch(typeof(AtlasZoneGenerator), "ProcessInterpreted")]
    internal static class AtlasZoneGeneratorProcessInterpretedPatch
    {
        private static bool Prefix(AtlasZoneGenerator __instance, AtlasInterpretation interpretation)
        {
            if (!FacilityLayout.TryGet(__instance.TargetZone, interpretation.Coords, out var entry))
                return true;
            return entry.OpType != OpType.Skip;
        }
    }

    /// <summary>
    /// Clones each atlas with staged pixel edits applied, swaps the clones
    /// into the generator's <see cref="AtlasZoneGenerator.Atlases"/> array
    /// for the duration of <see cref="AtlasZoneGenerator.Generate"/>, then
    /// restores the originals and destroys the clones. Keeps vanilla texture
    /// assets pristine across rounds.
    /// </summary>
    [HarmonyPatch(typeof(AtlasZoneGenerator), nameof(AtlasZoneGenerator.Generate))]
    internal static class AtlasZoneGeneratorGeneratePatch
    {
        private static bool Prefix(AtlasZoneGenerator __instance, out Texture2D[] __state)
        {
            __state = null;
            var atlases = __instance.Atlases;
            if (atlases == null || atlases.Length == 0) return true;

            Texture2D[] originals = null;
            for (int i = 0; i < atlases.Length; i++)
            {
                var edits = FacilityLayout.GetAtlasEdits(__instance.TargetZone, i);
                if (edits.Count == 0 || atlases[i] == null) continue;

                if (originals == null)
                {
                    originals = new Texture2D[atlases.Length];
                    for (int j = 0; j < atlases.Length; j++) originals[j] = atlases[j];
                }

                atlases[i] = CloneWithEdits(atlases[i], edits);
            }

            __state = originals; // null if no edits applied this generator
            return true;
        }

        private static void Postfix(AtlasZoneGenerator __instance, Texture2D[] __state)
        {
            if (__state == null) return;
            var atlases = __instance.Atlases;
            for (int i = 0; i < atlases.Length; i++)
            {
                if (atlases[i] == __state[i]) continue;
                var clone = atlases[i];
                atlases[i] = __state[i];
                if (clone != null) Object.Destroy(clone);
            }
        }

        private static Texture2D CloneWithEdits(Texture2D source, System.Collections.Generic.List<AtlasPixelEdit> edits)
        {
            var clone = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            clone.filterMode  = source.filterMode;
            clone.wrapMode    = source.wrapMode;
            clone.anisoLevel  = source.anisoLevel;
            clone.SetPixels32(source.GetPixels32());
            foreach (var e in edits)
            {
                if (e.PixelX >= clone.width || e.PixelY >= clone.height) continue;
                clone.SetPixel(e.PixelX, e.PixelY, new Color32(e.R, e.G, e.B, 255));
            }
            clone.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return clone;
        }
    }
}
