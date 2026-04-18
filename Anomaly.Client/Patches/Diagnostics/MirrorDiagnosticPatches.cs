using System;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppMirror;
using Il2CppRemoteAdmin;
using MelonLoader;

namespace Anomaly.Client.Patches.Diagnostics;

/// <summary>
/// Temporary diagnostic patches for investigating the post-auth spawn
/// failures and "Unknown message id: XXXX" disconnect.
///
/// Two jobs:
///
/// 1. Keep the client alive past the first unknown message. Vanilla Mirror
///    tears down the connection the moment <c>NetworkClient.UnpackAndInvoke</c>
///    returns false, which cascades into the NRE we've been seeing inside
///    <c>LiteNetLib4MirrorClient.OnNetworkReceive</c>. The outer
///    <c>GetNextMessage</c> loop reads each message into its own sub-reader,
///    so forcing the result to true doesn't corrupt subsequent messages.
///
/// 2. Dump <c>NetworkClient</c>'s prefab / handler / spawn-handler counts at
///    multiple lifecycle points and on the first few <c>OnSpawn</c> calls,
///    so we can tell whether the registry is empty, partial, or has the
///    specific <c>assetId</c> the server is asking for.
///
/// Remove or gate on a config flag once the root cause is understood.
/// </summary>
public static class MirrorDiagnosticPatches
{
    private static int _onSpawnLogCount;
    private static int _unknownMessageCount;

    private static void DumpState(string where)
    {
        int prefabs = -1;
        int handlers = -1;
        int spawnHandlers = -1;

        try { prefabs = NetworkClient.prefabs.Count; }
        catch (Exception ex) { MelonLogger.Warning($"[Anomaly Diag] read NetworkClient.prefabs failed: {ex.Message}"); }

        try { handlers = NetworkClient.handlers.Count; }
        catch (Exception ex) { MelonLogger.Warning($"[Anomaly Diag] read NetworkClient.handlers failed: {ex.Message}"); }

        try { spawnHandlers = NetworkClient.spawnHandlers.Count; }
        catch (Exception ex) { MelonLogger.Warning($"[Anomaly Diag] read NetworkClient.spawnHandlers failed: {ex.Message}"); }

        MelonLogger.Msg(
            $"[Anomaly Diag] {where}: " +
            $"prefabs={prefabs} handlers={handlers} spawnHandlers={spawnHandlers}");
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.RegisterClientMessages))]
    public static class OnRegisterClientMessagesDiag
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DumpState("after NetworkManager.RegisterClientMessages");
        }
    }

    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnStartClient))]
    public static class OnStartClientDiag
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            DumpOnClientStartedSubscribers("before OnStartClient");
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            DumpState("after CustomNetworkManager.OnStartClient");
        }
    }

    /// <summary>
    /// Reads the static delegate fields of <c>CustomNetworkManager</c> and
    /// logs their subscriber counts. The OnClientStarted event's backing
    /// field may be named differently under IL2CPP (e.g. compiler-generated
    /// `OnClientStarted` vs `k__BackingField`), so we enumerate all static
    /// delegate-typed fields rather than looking up one by name.
    /// </summary>
    private static void DumpOnClientStartedSubscribers(string where)
    {
        try
        {
            var type = typeof(CustomNetworkManager);
            var fields = type.GetFields(
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

            int delegateFieldCount = 0;
            foreach (var field in fields)
            {
                if (!typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
                delegateFieldCount++;

                var del = field.GetValue(null) as Delegate;
                int subs = del?.GetInvocationList()?.Length ?? 0;
                MelonLogger.Msg(
                    $"[Anomaly Diag] {where}: static delegate '{field.Name}' " +
                    $"({field.FieldType.Name}) subscribers={subs}");

                if (del != null && subs > 0)
                {
                    int shown = 0;
                    foreach (var d in del.GetInvocationList())
                    {
                        if (shown++ >= 12) break;
                        var declaring = d.Method?.DeclaringType?.FullName ?? "<?>";
                        var methodName = d.Method?.Name ?? "<?>";
                        MelonLogger.Msg($"[Anomaly Diag]   {field.Name}[{shown}]: {declaring}.{methodName}");
                    }
                }
            }

            if (delegateFieldCount == 0)
            {
                MelonLogger.Warning(
                    $"[Anomaly Diag] {where}: no static delegate fields on CustomNetworkManager — " +
                    $"IL2CPP may store event backing fields outside managed metadata");
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[Anomaly Diag] DumpOnClientStartedSubscribers({where}) failed: {ex}");
        }
    }

    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientConnect))]
    public static class OnClientConnectDiag
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DumpState("after CustomNetworkManager.OnClientConnect");
        }
    }

    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientSceneChanged))]
    public static class OnClientSceneChangedDiag
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DumpState("after CustomNetworkManager.OnClientSceneChanged");
        }
    }

    [HarmonyPatch(typeof(NetworkClient), "UnpackAndInvoke")]
    public static class SuppressUnpackDisconnect
    {
        /// <summary>
        /// Capture the reader's position so we can rewind in the postfix and
        /// re-read the message id if handler dispatch failed. Mirror calls
        /// <c>NetworkMessages.UnpackId</c> first (advances the reader by 2),
        /// then looks up the handler — if lookup fails it logs "Unknown
        /// message id: N" via Debug.LogWarning and returns false. By saving
        /// the pre-UnpackId position we can recover N ourselves without
        /// depending on that Debug log routing through MelonLogger.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(NetworkReader reader, out int __state)
        {
            try { __state = reader.Position; }
            catch { __state = -1; }
        }

        [HarmonyPostfix]
        public static void Postfix(NetworkReader reader, int channelId, int __state, ref bool __result)
        {
            if (__result) return;

            _unknownMessageCount++;

            // Try to recover the message id the dispatcher tried to look up.
            ushort msgId = 0;
            bool idKnown = false;
            if (__state >= 0)
            {
                try
                {
                    int currentPos = reader.Position;
                    reader.Position = __state;
                    msgId = reader.ReadUShort();
                    reader.Position = currentPos;
                    idKnown = true;
                }
                catch
                {
                    // Reader state manipulation failed; fall through with unknown id.
                }
            }

            if (_unknownMessageCount <= 30)
            {
                var idStr = idKnown ? msgId.ToString() : "<unreadable>";
                MelonLogger.Warning(
                    $"[Anomaly Diag] Unknown message id={idStr} on channel={channelId} " +
                    $"(#{_unknownMessageCount}) — suppressing Mirror disconnect");
            }
            else if (_unknownMessageCount == 31)
            {
                MelonLogger.Warning("[Anomaly Diag] Unknown-message failures exceed 30, silencing further warnings");
            }

            __result = true;
        }
    }

    /// <summary>
    /// Counts each time <c>SpawnableRoom.SetupNetIdHandlers</c> fires during
    /// facility generation — that's the method that calls
    /// <c>NetworkClient.RegisterSpawnHandler(assetId, ...)</c> for every
    /// NetworkIdentity inside each room duplicate. If facility generation
    /// were running normally we'd expect dozens of these per connect; if
    /// we see near-zero the client isn't generating rooms.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppMapGeneration.SpawnableRoom), "SetupNetIdHandlers")]
    public static class SetupNetIdHandlersDiag
    {
        private static int _setupCount;

        [HarmonyPrefix]
        public static void Prefix(Il2CppMapGeneration.SpawnableRoom __instance, int prevSpawnedCnt)
        {
            _setupCount++;
            if (_setupCount <= 10 || _setupCount % 25 == 0)
            {
                try
                {
                    int nidCount = __instance.OriginalIdentities?.Length ?? -1;
                    MelonLogger.Msg(
                        $"[Anomaly Diag] SpawnableRoom.SetupNetIdHandlers #{_setupCount}: " +
                        $"room='{__instance.name}' prevSpawned={prevSpawnedCnt} netIdCount={nidCount}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Anomaly Diag] SetupNetIdHandlersDiag log failed: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Dumps the full set of <c>NetworkClient.handlers</c> keys (Mirror
    /// message IDs) the first time <c>UnpackAndInvoke</c> succeeds, so the
    /// known-good handler set can be compared against the unknown-message
    /// list above. Compare client vs server: any id the server sends but the
    /// client lacks here is a registration gap.
    /// </summary>
    [HarmonyPatch(typeof(NetworkClient), "UnpackAndInvoke")]
    public static class DumpHandlerKeysOnce
    {
        private static bool _dumped;

        [HarmonyPostfix]
        public static void Postfix(bool __result)
        {
            if (_dumped || !__result) return;

            try
            {
                var handlers = NetworkClient.handlers;
                if (handlers == null) return;

                var sb = new System.Text.StringBuilder();
                int count = 0;
                foreach (var kv in handlers)
                {
                    if (count > 0) sb.Append(", ");
                    sb.Append(kv.Key);
                    count++;
                    if (count >= 128) break;
                }
                MelonLogger.Msg(
                    $"[Anomaly Diag] NetworkClient.handlers keys ({handlers.Count} total, first {count}): {sb}");
                _dumped = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Anomaly Diag] DumpHandlerKeysOnce failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NetworkClient), "OnSpawn")]
    public static class OnSpawnDiag
    {
        [HarmonyPrefix]
        public static void Prefix(SpawnMessage message)
        {
            _onSpawnLogCount++;
            if (_onSpawnLogCount > 3) return;

            try
            {
                uint assetId = 0;
                uint netId = 0;
                ulong sceneId = 0;
                try { assetId = message.assetId; } catch { }
                try { netId = message.netId; } catch { }
                try { sceneId = message.sceneId; } catch { }

                int total = -1;
                bool has = false;
                int spawnHandlerTotal = -1;
                bool hasSpawnHandler = false;
                try
                {
                    total = NetworkClient.prefabs.Count;
                    has = NetworkClient.prefabs.ContainsKey(assetId);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Anomaly Diag] prefabs probe failed: {ex.Message}");
                }

                try
                {
                    spawnHandlerTotal = NetworkClient.spawnHandlers.Count;
                    hasSpawnHandler = NetworkClient.spawnHandlers.ContainsKey(assetId);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Anomaly Diag] spawnHandlers probe failed: {ex.Message}");
                }

                MelonLogger.Msg(
                    $"[Anomaly Diag] OnSpawn #{_onSpawnLogCount}: " +
                    $"assetId={assetId} netId={netId} sceneId={sceneId} " +
                    $"prefabsRegistered={total} containsThisAsset={has} " +
                    $"spawnHandlersRegistered={spawnHandlerTotal} containsSpawnHandler={hasSpawnHandler}");

                // On the very first spawn, dump all registered assetIds so we
                // can see exactly what IS present and spot the gap.
                if (_onSpawnLogCount == 1)
                {
                    DumpPrefabKeys();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Anomaly Diag] OnSpawnDiag failed: {ex}");
            }
        }
    }

    private static void DumpPrefabKeys()
    {
        try
        {
            var dict = NetworkClient.prefabs;
            if (dict == null)
            {
                MelonLogger.Warning("[Anomaly Diag] prefabs dict is null");
                return;
            }

            var sb = new System.Text.StringBuilder();
            int count = 0;
            uint min = uint.MaxValue;
            uint max = uint.MinValue;

            foreach (var kv in dict)
            {
                count++;
                uint key = kv.Key;
                if (key < min) min = key;
                if (key > max) max = key;
                if (count <= 128)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(key);
                }
            }

            MelonLogger.Msg($"[Anomaly Diag] prefab key range: min={min} max={max} total={count}");
            MelonLogger.Msg($"[Anomaly Diag] prefab keys (first {Math.Min(count, 128)}): {sb}");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[Anomaly Diag] DumpPrefabKeys failed: {ex}");
        }
    }

    [HarmonyPatch(typeof(ServerRoles), "UserCode_TargetSetRemoteAdmin__Boolean")]
    public static class TargetSetRemoteAdminDiag
    {
        [HarmonyPostfix]
        public static void Postfix(bool open)
        {
            MelonLogger.Msg($"[Anomaly Diag] TargetSetRemoteAdmin received: open={open}");
        }
    }

    /// <summary>
    /// Cpp2IL renames the Mirror-weaver UserCode shim for TargetUpdateCommandList
    /// to <c>Method_Protected_Void_Il2CppReferenceArray_1_CommandData_PDM_0</c>
    /// (token <c>100668360</c>). The matching <c>InvokeUserCode_*</c> dispatcher
    /// is similarly renamed. Native dispatch is unaffected — only the managed
    /// shim's name changes — so this patch fires whenever the RPC body runs.
    /// We resolve by name first, then by token, then no-op rather than letting
    /// Harmony abort PatchAll on a missing target.
    /// </summary>
    [HarmonyPatch]
    public static class TargetUpdateCommandListDiag
    {
        public static MethodBase TargetMethod()
        {
            var t = typeof(QueryProcessor);
            var byName = AccessTools.Method(t, "Method_Protected_Void_Il2CppReferenceArray_1_CommandData_PDM_0");
            if (byName != null) return byName;

            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (m.MetadataToken == 100668360) return m;
            }

            MelonLogger.Warning("[Anomaly Diag] TargetUpdateCommandList shim not found by name OR token; diagnostic patch skipped.");
            return null;
        }

        [HarmonyPostfix]
        public static void Postfix(object[] __args)
        {
            int count = -1;
            try
            {
                if (__args != null && __args.Length > 0 && __args[0] != null)
                {
                    if (__args[0] is Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<QueryProcessor.CommandData> arr)
                        count = arr.Length;
                    else
                        count = -2; // Unexpected arg type — kept distinct from null/-1 for diagnosis.
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Anomaly Diag] TargetUpdateCommandList arg probe failed: {ex.Message}");
            }
            MelonLogger.Msg($"[Anomaly Diag] TargetUpdateCommandList received: count={count}");
        }
    }

    [HarmonyPatch(typeof(QueryProcessor), "UserCode_TargetSyncGameplayData__NetworkConnection__Boolean")]
    public static class TargetSyncGameplayDataDiag
    {
        [HarmonyPostfix]
        public static void Postfix(bool gd)
        {
            MelonLogger.Msg($"[Anomaly Diag] TargetSyncGameplayData received: gameplayData={gd}");
        }
    }

    /// <summary>
    /// Logs every outbound encrypted channel message so we can identify what's
    /// being sent during the "every-second reconnect" pattern in RA. Postfix so
    /// it runs even though our own Auth patch's prefix returns false to skip
    /// vanilla — Harmony postfixes always run regardless.
    /// </summary>
    [HarmonyPatch(typeof(EncryptedChannelManager), nameof(EncryptedChannelManager.TrySendMessageToServer), new Type[] { typeof(string), typeof(EncryptedChannelManager.EncryptedChannel) })]
    public static class TrySendMessageToServerDiag
    {
        private static int _count;

        [HarmonyPostfix]
        public static void Postfix(string content, EncryptedChannelManager.EncryptedChannel channel)
        {
            try
            {
                if (++_count > 50 && _count % 25 != 0) return;
                string text = content ?? string.Empty;
                if (text.Length > 80) text = text.Substring(0, 80) + "...";
                MelonLogger.Msg($"[Anomaly Diag] TrySendMessageToServer #{_count}: channel={channel} content='{text}'");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Anomaly Diag] TrySendMessageToServerDiag failed: {ex.Message}");
            }
        }
    }
}
