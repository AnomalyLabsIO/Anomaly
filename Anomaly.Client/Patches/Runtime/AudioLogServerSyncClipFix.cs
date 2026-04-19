using HarmonyLib;
using Il2CppMapGeneration.Spawnables;
using MelonLoader;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Defensive prefix on <c>AudioLog.ServerSyncClip</c> that swallows the NRE
/// thrown when the static <c>ReferenceHub.OnPlayerAdded</c> dispatches to a
/// destroyed <c>AudioLog</c> instance.
///
/// <para><b>Why this leaks (vanilla cleanup-order bug).</b></para>
/// <list type="number">
///   <item>User hosts a local server. <c>AudioLog.Awake</c> subscribes
///         <c>ServerSyncClip</c> to <c>ReferenceHub.OnPlayerAdded</c>, gated on
///         <c>NetworkServer.active == true</c>.</item>
///   <item>User stops hosting. Mirror's <c>NetworkServer.Shutdown()</c> sets
///         <c>active = false</c> before the scene tears down.</item>
///   <item>Scene unloads. <c>AudioLog.OnDestroy</c> runs but its unsubscribe is
///         gated on the now-false <c>NetworkServer.active</c>, so it skips the
///         <c>OnPlayerAdded -= ServerSyncClip</c> step.</item>
///   <item>Stale delegates pointing at destroyed instances stay in the static
///         event.</item>
///   <item>User connects to a dedicated server. The local hub's
///         <c>ReferenceHub.Start</c> fires <c>OnPlayerAdded</c>; the stale
///         delegates dispatch to AudioLogs whose <c>_audioSource</c> is null;
///         <c>IsPlaying</c> NREs.</item>
/// </list>
///
/// We can't fix the OnDestroy gate from the client side without intercepting
/// Mirror's host shutdown ordering. The simpler, surgical fix: when
/// ServerSyncClip is invoked, verify the instance is still alive before
/// running vanilla. The dangling delegate stays in the event but each
/// dispatch costs only the cheap null check.
/// </summary>
[HarmonyPatch(typeof(AudioLog), "ServerSyncClip")]
public static class AudioLogServerSyncClipFix
{
    private static int _suppressedCount;

    [HarmonyPrefix]
    public static bool Prefix(AudioLog __instance)
    {
        try
        {
            if (__instance == null || __instance._audioSource == null)
            {
                if (++_suppressedCount <= 3)
                    MelonLogger.Msg($"[AudioLogFix] Suppressed ServerSyncClip on dangling AudioLog (#{_suppressedCount}).");
                return false;
            }
        }
        catch
        {
            return false;
        }
        return true;
    }
}
