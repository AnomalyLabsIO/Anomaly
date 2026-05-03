using System.Collections.Generic;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Assets;
using HarmonyLib;
using Il2CppCassie;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Patches.Audio
{
    /// <summary>
    /// Harmony prefix on <see cref="CassieLineSource.Schedule"/> that
    /// substitutes <c>cassieLine.Clip</c> with a player- or server-supplied
    /// override before vanilla schedules playback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Patched at <see cref="CassieLineSource.Schedule"/> rather than at
    /// <see cref="CassieLineDatabase.TryGetLine"/> because the database method
    /// takes a <c>ReadOnlySpan&lt;char&gt;</c>, which crosses the Il2Cpp
    /// boundary awkwardly. <c>Schedule</c> takes a reference-type
    /// <see cref="CassieLine"/> with a directly-mutable <c>Clip</c> field —
    /// straightforward to patch and surgical (one method, all consumers).
    /// </para>
    /// <para>
    /// The same <see cref="CassieLine"/> instance is reused across every play
    /// of a given ApiName, so once we mutate <c>.Clip</c> the override sticks.
    /// We capture the original clip in <see cref="_originalClips"/> on first
    /// substitution, so <see cref="RestoreAll"/> can revert all mutations on
    /// disconnect — keeping the engine's database clean for the next session.
    /// </para>
    /// </remarks>
    [HarmonyPatch(typeof(CassieLineSource), nameof(CassieLineSource.Schedule))]
    public static class CassieLineSourceSchedulePatch
    {
        private static readonly Dictionary<CassieLine, AudioClip> _originalClips = new();

        [HarmonyPrefix]
        public static void Prefix(CassieLine cassieLine)
        {
            if (cassieLine == null || string.IsNullOrEmpty(cassieLine.ApiName))
                return;

            try
            {
                if (!PathOverrideRegistry.TryGetAudioClip(cassieLine.ApiName, out var overrideClip))
                    return;
                if (overrideClip == null)
                    return;

                if (!_originalClips.ContainsKey(cassieLine))
                    _originalClips[cassieLine] = cassieLine.Clip;

                if (cassieLine.Clip != overrideClip)
                    cassieLine.Clip = overrideClip;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"CassieLineSourceSchedulePatch.Prefix('{cassieLine?.ApiName}'): {ex}");
            }
        }

        /// <summary>
        /// Fires <see cref="AnnouncerEvents.LineScheduled"/> after vanilla
        /// scheduling has accepted the line. Runs after the prefix so the
        /// final (possibly-overridden) clip is what the event observes.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(CassieLine cassieLine, double startDspTime)
        {
            if (cassieLine == null)
                return;

            try
            {
                AnnouncerEvents.OnLineScheduled(new AnnouncerLineScheduledEventArgs(cassieLine, startDspTime));
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"CassieLineSourceSchedulePatch.Postfix('{cassieLine?.ApiName}'): {ex}");
            }
        }

        /// <summary>
        /// Restores every CASSIE line we mutated to its pre-override clip.
        /// Called from the disconnect handler so the next session starts with
        /// the database in its vanilla state.
        /// </summary>
        public static void RestoreAll()
        {
            foreach (var kv in _originalClips)
            {
                if (kv.Key != null)
                    kv.Key.Clip = kv.Value;
            }
            _originalClips.Clear();
        }
    }
}
