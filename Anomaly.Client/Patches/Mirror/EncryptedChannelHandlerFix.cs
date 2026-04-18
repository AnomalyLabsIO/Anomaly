using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppMirror;
using MelonLoader;

namespace Anomaly.Client.Patches.Mirror;

/// <summary>
/// Registers Mirror's handler for <c>EncryptedChannelManager+EncryptedMessageOutside</c>
/// (message id 9934 under <c>GetStableHashCode</c>) eagerly at
/// <c>NetworkManager.RegisterClientMessages</c>-time, bypassing the fact
/// that vanilla defers this to <c>EncryptedChannelManager.Start()</c> (only
/// fires when the local player's hub spawns — too late).
///
/// Can't go through <c>NetworkClient.ReplaceHandler&lt;EncryptedMessageOutside&gt;</c>
/// because Il2CppInterop rejects delegates whose parameters are non-blittable
/// IL2CPP structs ("Delegate has parameter of type ...+EncryptedMessageOutside
/// (non-blittable struct) which is not supported"). Instead we synthesize a
/// <c>NetworkMessageDelegate</c> (signature uses only class refs + int — all
/// blittable-compatible) and write it straight into the private internal
/// <c>NetworkClient.handlers</c> dictionary. Our delegate body reflectively:
///
/// 1. Calls the Weaver-generated <c>EncryptedChannelFunctions.DeserializeEncryptedMessageOutside(reader)</c>
///    to read the struct — reflection unboxes it for us.
/// 2. Passes the boxed struct to the private static
///    <c>EncryptedChannelManager.ClientReceivePackedMessage</c>. The existing
///    Anomaly Harmony prefix on that method still intercepts and does the
///    decrypt + Anomaly dispatch.
/// </summary>
public static class EncryptedChannelHandlerFix
{
    private static MethodInfo _clientReceiveMethod;
    private static MethodInfo _deserializeMethod;
    private static NetworkMessageDelegate _bridgeDelegate;

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.RegisterClientMessages))]
    public static class RegisterEncryptedHandler
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                // Cache the method lookups and the converted bridge delegate
                // once — they don't change across connects. But re-apply the
                // dictionary entry on every RegisterClientMessages since the
                // NetworkClient.handlers dict gets cleared on each disconnect
                // (and without re-applying, reconnecting leaves id 9934 with
                // no handler again).
                if (_clientReceiveMethod == null)
                {
                    _clientReceiveMethod = typeof(EncryptedChannelManager).GetMethod(
                        "ClientReceivePackedMessage",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (_clientReceiveMethod == null)
                    {
                        MelonLogger.Warning("[EncryptedChannelHandlerFix] ClientReceivePackedMessage not found");
                        return;
                    }
                }

                if (_deserializeMethod == null)
                {
                    _deserializeMethod = FindDeserializeMethod();
                    if (_deserializeMethod == null)
                    {
                        MelonLogger.Warning("[EncryptedChannelHandlerFix] DeserializeEncryptedMessageOutside not found");
                        return;
                    }
                }

                if (_bridgeDelegate == null)
                {
                    Action<NetworkConnection, NetworkReader, int> managedBridge = BridgeDispatch;
                    _bridgeDelegate = DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(managedBridge);
                }

                var dict = NetworkClient.handlers;
                if (dict == null)
                {
                    MelonLogger.Warning("[EncryptedChannelHandlerFix] NetworkClient.handlers is null");
                    return;
                }

                dict[(ushort)9934] = _bridgeDelegate;

                MelonLogger.Msg(
                    $"[EncryptedChannelHandlerFix] Installed handler for EncryptedMessageOutside " +
                    $"(id=9934) — NetworkClient.handlers now has {dict.Count} entries");
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                MelonLogger.Error(
                    $"[EncryptedChannelHandlerFix] Failed to register handler: " +
                    $"{inner.GetType().Name}: {inner.Message}");
            }
        }
    }

    /// <summary>
    /// Searches loaded assemblies for <c>EncryptedChannelFunctions.DeserializeEncryptedMessageOutside</c>.
    /// This is a Weaver-generated static helper registered into
    /// <c>Reader&lt;EncryptedMessageOutside&gt;.read</c>.
    /// </summary>
    private static MethodInfo FindDeserializeMethod()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t;
            try { t = asm.GetType("Il2CppMirror.EncryptedChannelFunctions", throwOnError: false); }
            catch { t = null; }
            if (t == null)
            {
                try { t = asm.GetType("EncryptedChannelFunctions", throwOnError: false); } catch { }
            }
            if (t == null) continue;

            var m = t.GetMethod("DeserializeEncryptedMessageOutside",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null) return m;
        }
        // Last resort: scan every loaded type for any static method by name
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(x => x != null).ToArray(); }
            catch { continue; }
            foreach (var t in types)
            {
                var m = t.GetMethod("DeserializeEncryptedMessageOutside",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null) return m;
            }
        }
        return null;
    }

    /// <summary>
    /// Installed as the <c>NetworkMessageDelegate</c> for id 9934. Reads the
    /// struct via the Weaver-generated deserializer (boxed via reflection,
    /// which sidesteps the Il2CppInterop blittable-struct restriction) and
    /// forwards to <c>ClientReceivePackedMessage</c> so our existing Harmony
    /// prefix can intercept.
    /// </summary>
    private static void BridgeDispatch(NetworkConnection conn, NetworkReader reader, int channelId)
    {
        try
        {
            var packed = _deserializeMethod.Invoke(null, new object[] { reader });
            _clientReceiveMethod.Invoke(null, new object[] { packed });
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            MelonLogger.Error(
                $"[EncryptedChannelHandlerFix] bridge dispatch failed: " +
                $"{inner.GetType().Name}: {inner.Message}");
        }
    }
}
