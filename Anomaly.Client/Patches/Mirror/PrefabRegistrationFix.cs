using System;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace Anomaly.Client.Patches.Mirror;

/// <summary>
/// Workaround for MelonLoader IL2CPP not firing <c>[RuntimeInitializeOnLoadMethod]</c>
/// on game-internal classes AND the corresponding <c>Init()</c>-invoked
/// <c>+=</c> subscriptions to <c>CustomNetworkManager.OnClientStarted</c>
/// failing to register in IL2CPP's native event storage (the event's backing
/// isn't exposed as a managed static field, and the managed <c>add_</c>
/// accessor proxy doesn't appear to persist the subscription).
///
/// Instead of routing through the event, we invoke the prefab-registration
/// handler methods directly as a postfix on
/// <c>CustomNetworkManager.OnStartClient</c>. This matches the point where
/// vanilla's <c>OnClientStarted?.Invoke()</c> would have called them.
/// Handlers are idempotent — Mirror's <c>RegisterPrefab</c> either no-ops
/// or warns on duplicate ids, and each handler's registration set is
/// rebuilt fresh per call.
/// </summary>
[HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnStartClient))]
public static class PrefabRegistrationFix
{
    private static bool _initsInvoked;

    /// <summary>
    /// SCP:SL handlers that register prefabs or client message handlers.
    /// These are the private-static parameterless bodies that the vanilla
    /// <c>OnClientStarted</c> event would have invoked after the Init
    /// methods' <c>+=</c> subscriptions — we skip the event and call them
    /// ourselves.
    ///
    /// <c>RagdollManager</c> and <c>Scp939AmnesticCloudAbility</c> subscribe
    /// via anonymous lambdas rather than named methods, so they can't be
    /// dispatched directly by name — handled separately if needed.
    /// </summary>
    private static readonly (string type, string method)[] _handlers =
    {
        ("Il2CppInventorySystem.InventoryItemLoader", "RegisterPrefabs"),
        ("Il2CppInventorySystem.Items.Thirdperson.ThirdpersonItemPoolManager", "SetupPools"),
        ("Il2CppInventorySystem.Items.ThrowableProjectiles.ThrowableNetworkHandler", "RegisterProjectiles"),
        ("Il2CppMapGeneration.Distributors.SpawnablesDistributorSettings", "RegisterSpawnables"),
        ("Il2CppMapGeneration.RoomConnectors.RoomConnectorDistributorSettings", "RegisterSpawnables"),
    };

    /// <summary>
    /// Types whose <c>[RuntimeInitializeOnLoadMethod]</c> <c>Init()</c>
    /// method does more than just subscribe to OnClientStarted — they also
    /// register additional message handlers (OnClientReady), load settings
    /// from Resources, or subscribe to UserSetting listeners. We still
    /// invoke these once for their side effects even though we bypass the
    /// event system for prefab registration itself.
    /// </summary>
    private static readonly string[] _oneTimeInitTypes =
    {
        "Il2CppInventorySystem.Items.ThrowableProjectiles.ThrowableNetworkHandler", // OnClientReady handler replacements
        "Il2CppMapGeneration.SeedSynchronizer",                                     // SeedMessage handler replace
        "Il2CppMapGeneration.RoomConnectors.RoomConnectorDistributorSettings",      // LoadSettingsFromResources()
        "Il2CppPlayerRoles.Ragdolls.RagdollManager",                                // UserSetting listeners
    };

    [HarmonyPrefix]
    public static void Prefix()
    {
        if (_initsInvoked) return;
        _initsInvoked = true;

        MelonLogger.Msg("[PrefabRegistrationFix] One-time Init() invocation for side effects");
        foreach (var typeName in _oneTimeInitTypes)
        {
            TryInvokeStatic(typeName, "Init");
        }
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        int ok = 0;
        int failed = 0;
        int prefabsBefore = SafeCountPrefabs();

        // Probe the item database state — if AvailableItems.Count is 0, the
        // handlers will run successfully but register zero prefabs because
        // their inner foreach has nothing to iterate.
        ProbeDatabaseState("before handlers");

        foreach (var (typeName, methodName) in _handlers)
        {
            int before = SafeCountPrefabs();
            if (TryInvokeStatic(typeName, methodName))
            {
                int after = SafeCountPrefabs();
                MelonLogger.Msg($"[PrefabRegistrationFix]   {typeName}.{methodName}: prefabs {before} -> {after}");
                ok++;
            }
            else
            {
                failed++;
            }
        }

        ProbeDatabaseState("after handlers");

        // Ragdoll prefabs register from a HashSet<NetworkIdentity> member.
        RegisterRagdollPrefabs();

        int prefabsAfter = SafeCountPrefabs();
        MelonLogger.Msg(
            $"[PrefabRegistrationFix] OnStartClient postfix: " +
            $"{ok} handlers invoked, {failed} failed. " +
            $"NetworkClient.prefabs {prefabsBefore} -> {prefabsAfter}");
    }

    /// <summary>
    /// Logs the state of the game's item / map-spawnable databases so we can
    /// tell whether the handlers are running against empty collections.
    /// </summary>
    private static void ProbeDatabaseState(string where)
    {
        try
        {
            int availableItems = Il2CppInventorySystem.InventoryItemLoader.AvailableItems?.Count ?? -1;
            MelonLogger.Msg(
                $"[PrefabRegistrationFix] {where}: " +
                $"InventoryItemLoader.AvailableItems.Count={availableItems}");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[PrefabRegistrationFix] ProbeDatabaseState({where}) failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int SafeCountPrefabs()
    {
        try { return Il2CppMirror.NetworkClient.prefabs.Count; }
        catch { return -1; }
    }

    private static bool TryInvokeStatic(string typeFullName, string methodName)
    {
        try
        {
            var type = ResolveType(typeFullName);
            if (type == null)
            {
                MelonLogger.Warning($"[PrefabRegistrationFix] Type not found: {typeFullName}");
                return false;
            }

            var method = type.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            if (method == null)
            {
                MelonLogger.Warning($"[PrefabRegistrationFix] Method not found: {typeFullName}.{methodName}");
                return false;
            }

            method.Invoke(null, null);
            return true;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            MelonLogger.Error(
                $"[PrefabRegistrationFix] {typeFullName}.{methodName} threw: " +
                $"{inner.GetType().Name}: {inner.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reimplements <c>RagdollManager</c>'s anonymous OnClientStarted handler
    /// (<c>AllRagdollPrefabs.ForEach(RegisterPrefab)</c>) using typed IL2CPP
    /// access so the IL2CPP HashSet enumerator works. The member is private
    /// so we still need reflection to get the getter — but we cast the
    /// result to the typed Il2CppSystem HashSet and iterate directly.
    /// </summary>
    private static void RegisterRagdollPrefabs()
    {
        try
        {
            var rmType = typeof(Il2CppPlayerRoles.Ragdolls.RagdollManager);

            var allProp = rmType.GetProperty("AllRagdollPrefabs",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var registerMethod = rmType.GetMethod("RegisterPrefab",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(Il2CppMirror.NetworkIdentity) },
                modifiers: null);

            if (allProp == null || registerMethod == null)
            {
                MelonLogger.Warning(
                    $"[PrefabRegistrationFix] RagdollManager members not found " +
                    $"(AllRagdollPrefabs={allProp != null}, RegisterPrefab={registerMethod != null})");
                return;
            }

            var raw = allProp.GetValue(null);
            if (raw == null)
            {
                MelonLogger.Warning("[PrefabRegistrationFix] RagdollManager.AllRagdollPrefabs returned null");
                return;
            }

            var hashSet = raw as Il2CppSystem.Collections.Generic.HashSet<Il2CppMirror.NetworkIdentity>;
            if (hashSet == null)
            {
                MelonLogger.Warning(
                    $"[PrefabRegistrationFix] RagdollManager.AllRagdollPrefabs is " +
                    $"{raw.GetType().FullName}, not the expected Il2CppSystem.Collections.Generic.HashSet<NetworkIdentity>");
                return;
            }

            int count = 0;
            int directRegistered = 0;
            int zeroAssetId = 0;
            int alreadyRegistered = 0;
            int prefabsBefore = SafeCountPrefabs();

            foreach (var nid in hashSet)
            {
                if (nid == null) continue;
                count++;

                // Diagnostic path: bypass the private RagdollManager.RegisterPrefab
                // entirely and call Il2CppMirror.NetworkClient.RegisterPrefab with
                // the NetworkIdentity's gameObject directly. If reflection Invoke
                // wasn't actually routing to the IL2CPP body, this direct typed
                // call should — and we'll see prefabs jump by whatever fraction
                // of the 8 ragdolls have a non-zero assetId.
                try
                {
                    uint aid = nid.assetId;
                    var go = nid.gameObject;
                    if (go == null)
                    {
                        MelonLogger.Warning($"[PrefabRegistrationFix]   ragdoll[{count}]: gameObject is null");
                        continue;
                    }
                    if (aid == 0)
                    {
                        zeroAssetId++;
                        MelonLogger.Msg($"[PrefabRegistrationFix]   ragdoll[{count}] name='{nid.name}' assetId=0 (Mirror will reject)");
                        continue;
                    }
                    if (Il2CppMirror.NetworkClient.prefabs.ContainsKey(aid))
                    {
                        alreadyRegistered++;
                        continue;
                    }
                    Il2CppMirror.NetworkClient.RegisterPrefab(go);
                    directRegistered++;
                    MelonLogger.Msg($"[PrefabRegistrationFix]   ragdoll[{count}] name='{nid.name}' assetId={aid} registered via direct call");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PrefabRegistrationFix]   ragdoll[{count}] direct register failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            int prefabsAfter = SafeCountPrefabs();
            MelonLogger.Msg(
                $"[PrefabRegistrationFix]   RagdollManager: iterated {count} ragdolls " +
                $"(zero-assetId={zeroAssetId}, already-registered={alreadyRegistered}, " +
                $"newly-registered-directly={directRegistered}); " +
                $"prefabs {prefabsBefore} -> {prefabsAfter}");
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            MelonLogger.Error($"[PrefabRegistrationFix] RegisterRagdollPrefabs failed: {inner.GetType().Name}: {inner.Message}");
        }
    }

    private static Type ResolveType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(fullName, throwOnError: false);
                if (t != null) return t;
            }
            catch
            {
                // Some IL2CPP proxy assemblies throw on reflection; ignore and continue.
            }
        }
        return null;
    }
}
