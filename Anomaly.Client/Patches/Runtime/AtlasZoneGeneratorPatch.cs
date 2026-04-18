using System;
using Anomaly.Client.Internal.Map;
using Anomaly.Shared.Networking.Messages;
using HarmonyLib;
using Il2CppMapGeneration;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Applies <see cref="FacilityRecipeClient"/> mutations during the client's
/// deterministic <c>AtlasZoneGenerator.Generate</c> pass. Mirrors the
/// patches in <c>Anomaly.Server.Map.Patches.AtlasZoneGeneratorPatch</c> so
/// the server and client produce the same facility.
///
/// All patch bodies are wrapped in try/catch because Il2Cpp's trampoline
/// re-enters managed code via reverse P/Invoke and an escaping exception
/// there produces a flood of "During invoking native->managed trampoline"
/// log lines (one per call), bulking up crash reports and masking the
/// original failure. A caught exception is logged once per unique site.
/// </summary>
public static class AtlasZoneGeneratorPatch
{
    // TEMPORARILY DISABLED: the postfixes below patch IL2CPP virtual methods
    // whose parameter-unwrap trampoline is NRE-ing on every call (50+ per
    // second during facility generation), bulking up crash reports and
    // obscuring the real investigation. The features that depend on these
    // (FacilityLayout room move/rotate/skip, atlas pixel edits) silently
    // become no-ops while this is false. Re-enable once the trampoline
    // issue is understood.
    private const bool PatchesEnabled = false;

    private static bool _loggedGetPosNre;
    private static bool _loggedProcessNre;
    private static bool _loggedGeneratePrefixNre;
    private static bool _loggedGeneratePostfixNre;

    [HarmonyPatch(typeof(AtlasZoneGenerator), nameof(AtlasZoneGenerator.GetPositionAndRotation))]
    public static class GetPositionAndRotation
    {
        [HarmonyPrepare]
        public static bool Prepare() => PatchesEnabled;

        [HarmonyPostfix]
        public static void Postfix(AtlasZoneGenerator __instance, AtlasInterpretation toSpawn,
                                   ref Vector3 worldPosition, ref float yRotation)
        {
            try
            {
                if (__instance == null || toSpawn == null) return;
                if (!FacilityRecipeClient.TryGet(__instance.TargetZone, toSpawn.Coords, out var entry)) return;
                if (entry.OpType != FacilityLayoutRecipeMessage.OpTransformOverride) return;

                if ((entry.DirtyMask & FacilityLayoutRecipeMessage.DirtyPosition) != 0)
                    worldPosition += new Vector3(entry.OffsetX, entry.OffsetY, entry.OffsetZ);

                if ((entry.DirtyMask & FacilityLayoutRecipeMessage.DirtyRotation) != 0)
                    yRotation = entry.RotationY;
            }
            catch (Exception ex)
            {
                if (!_loggedGetPosNre)
                {
                    _loggedGetPosNre = true;
                    MelonLogger.Error(
                        $"AtlasZoneGeneratorPatch.GetPositionAndRotation threw (silencing further): " +
                        $"__instance={(__instance == null ? "null" : "ok")} " +
                        $"toSpawn={(toSpawn == null ? "null" : "ok")} " +
                        $"error={ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(AtlasZoneGenerator), "ProcessInterpreted")]
    public static class ProcessInterpreted
    {
        [HarmonyPrepare]
        public static bool Prepare() => PatchesEnabled;

        [HarmonyPrefix]
        public static bool Prefix(AtlasZoneGenerator __instance, AtlasInterpretation interpretation)
        {
            try
            {
                if (__instance == null || interpretation == null) return true;
                if (!FacilityRecipeClient.TryGet(__instance.TargetZone, interpretation.Coords, out var entry))
                    return true;
                return entry.OpType != FacilityLayoutRecipeMessage.OpSkip;
            }
            catch (Exception ex)
            {
                if (!_loggedProcessNre)
                {
                    _loggedProcessNre = true;
                    MelonLogger.Error(
                        $"AtlasZoneGeneratorPatch.ProcessInterpreted threw (silencing further): " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }
                return true;
            }
        }
    }

    /// <summary>
    /// Clones atlas textures with staged pixel edits applied and swaps them
    /// into <see cref="AtlasZoneGenerator.Atlases"/> for the duration of
    /// <see cref="AtlasZoneGenerator.Generate"/>. Mirrors the server patch
    /// in <c>Anomaly.Server.Map.Patches.AtlasZoneGeneratorGeneratePatch</c>
    /// so both sides hand <c>MapAtlasInterpreter.Interpret</c> byte-identical
    /// textures.
    /// </summary>
    [HarmonyPatch(typeof(AtlasZoneGenerator), nameof(AtlasZoneGenerator.Generate))]
    public static class Generate
    {
        [HarmonyPrepare]
        public static bool Prepare() => PatchesEnabled;

        [HarmonyPrefix]
        public static void Prefix(AtlasZoneGenerator __instance, out Texture2D[] __state)
        {
            __state = null;
            try
            {
                if (__instance == null) return;
                var atlases = __instance.Atlases;
                if (atlases == null || atlases.Length == 0) return;

                Texture2D[] originals = null;
                for (int i = 0; i < atlases.Length; i++)
                {
                    var edits = FacilityRecipeClient.GetAtlasEdits(__instance.TargetZone, i);
                    if (edits.Count == 0 || atlases[i] == null) continue;

                    if (originals == null)
                    {
                        originals = new Texture2D[atlases.Length];
                        for (int j = 0; j < atlases.Length; j++) originals[j] = atlases[j];
                    }
                    atlases[i] = CloneWithEdits(atlases[i], edits);
                }
                __state = originals;
            }
            catch (Exception ex)
            {
                if (!_loggedGeneratePrefixNre)
                {
                    _loggedGeneratePrefixNre = true;
                    MelonLogger.Error(
                        $"AtlasZoneGeneratorPatch.Generate.Prefix threw (silencing further): " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix(AtlasZoneGenerator __instance, Texture2D[] __state)
        {
            try
            {
                if (__state == null || __instance == null) return;
                var atlases = __instance.Atlases;
                if (atlases == null) return;
                for (int i = 0; i < atlases.Length; i++)
                {
                    if (atlases[i] == __state[i]) continue;
                    var clone = atlases[i];
                    atlases[i] = __state[i];
                    if (clone != null) UnityEngine.Object.Destroy(clone);
                }
            }
            catch (Exception ex)
            {
                if (!_loggedGeneratePostfixNre)
                {
                    _loggedGeneratePostfixNre = true;
                    MelonLogger.Error(
                        $"AtlasZoneGeneratorPatch.Generate.Postfix threw (silencing further): " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static Texture2D CloneWithEdits(Texture2D source, System.Collections.Generic.List<AtlasPixelEdit> edits)
        {
            var clone = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            clone.filterMode = source.filterMode;
            clone.wrapMode   = source.wrapMode;
            clone.anisoLevel = source.anisoLevel;
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
