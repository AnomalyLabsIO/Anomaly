using System;
using System.Linq;
using System.Reflection;
using Anomaly.Client.Api.Internal;
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
    private static NetworkMessageDelegate _previousDelegate;
    private static IntPtr _publishedDictionaryPointer;
    private static bool _hadPreviousDelegate;
    private static bool _published;
    private static readonly TransactionalInitializer Initializer = new();

    internal static bool IsReady { get; private set; }
    internal static string LastError { get; private set; }

    /// <summary>
    /// Resolves the required bridge and publishes it into the current client
    /// handler dictionary. This is called directly by the required networking
    /// registrar so Harmony postfix ordering cannot report networking ready
    /// before message id 9934 is actually installed.
    /// </summary>
    internal static bool EnsureRegistered()
    {
        IsReady = false;
        try
        {
            Initializer.Initialize(
                new InitializationStep(
                    () =>
                    {
                    _clientReceiveMethod = typeof(EncryptedChannelManager).GetMethod(
                        "ClientReceivePackedMessage",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (_clientReceiveMethod == null)
                            throw new MissingMethodException(
                                typeof(EncryptedChannelManager).FullName,
                                "ClientReceivePackedMessage");
                    },
                    () => _clientReceiveMethod = null),
                new InitializationStep(
                    () =>
                    {
                    _deserializeMethod = FindDeserializeMethod();
                    if (_deserializeMethod == null)
                            throw new MissingMethodException(
                                "EncryptedChannelFunctions",
                                "DeserializeEncryptedMessageOutside");
                    },
                    () => _deserializeMethod = null),
                new InitializationStep(
                    () =>
                    {
                    Action<NetworkConnection, NetworkReader, int> managedBridge = BridgeDispatch;
                    _bridgeDelegate = DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(managedBridge);
                    if (_bridgeDelegate == null)
                            throw new InvalidOperationException("IL2CPP delegate conversion returned null.");
                    },
                    () => _bridgeDelegate = null));

            var handlers = NetworkClient.handlers ??
                throw new InvalidOperationException("NetworkClient.handlers is null.");

            if (handlers.TryGetValue((ushort)9934, out NetworkMessageDelegate existing) &&
                SameDelegate(existing, _bridgeDelegate))
            {
                _published = true;
                _publishedDictionaryPointer = handlers.Pointer;
                _hadPreviousDelegate = false;
                _previousDelegate = null;
            }
            else
            {
                _hadPreviousDelegate = handlers.TryGetValue((ushort)9934, out _previousDelegate);
                _publishedDictionaryPointer = handlers.Pointer;
                try
                {
                    handlers[(ushort)9934] = _bridgeDelegate;
                    _published = true;
                    if (!handlers.TryGetValue((ushort)9934, out NetworkMessageDelegate installed) ||
                        !SameDelegate(installed, _bridgeDelegate))
                    {
                        throw new InvalidOperationException(
                            "Encrypted message handler publication could not be verified.");
                    }
                }
                catch
                {
                    RollBackConnectionRegistration();
                    throw;
                }
            }

            IsReady = true;
            LastError = null;
            SafeLogMessage(
                $"[EncryptedChannelHandlerFix] Installed handler for EncryptedMessageOutside " +
                $"(id=9934) — NetworkClient.handlers now has {handlers.Count} entries");
            return true;
        }
        catch (Exception ex)
        {
            RollBackConnectionRegistration();
            Initializer.Reset();
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            LastError = $"{inner.GetType().Name}: {inner.Message}";
            SafeLogError("[EncryptedChannelHandlerFix] Failed to register handler: " + LastError);
            return false;
        }
    }

    internal static void ResetConnectionState()
    {
        RollBackConnectionRegistration();
        IsReady = false;
    }

    private static void RollBackConnectionRegistration()
    {
        try
        {
            var handlers = NetworkClient.handlers;
            if (_published &&
                handlers != null &&
                handlers.Pointer == _publishedDictionaryPointer &&
                handlers.TryGetValue((ushort)9934, out NetworkMessageDelegate current) &&
                SameDelegate(current, _bridgeDelegate))
            {
                if (_hadPreviousDelegate)
                    handlers[(ushort)9934] = _previousDelegate;
                else
                    handlers.Remove((ushort)9934);
            }
        }
        catch { }
        finally
        {
            _published = false;
            _publishedDictionaryPointer = IntPtr.Zero;
            _hadPreviousDelegate = false;
            _previousDelegate = null;
        }
    }

    private static bool SameDelegate(NetworkMessageDelegate first, NetworkMessageDelegate second)
    {
        return ReferenceEquals(first, second) ||
            (first != null && second != null && first.Pointer == second.Pointer);
    }

    private static void SafeLogMessage(string message)
    {
        try { MelonLogger.Msg(message); }
        catch { }
    }

    private static void SafeLogError(string message)
    {
        try { MelonLogger.Error(message); }
        catch { }
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
            Anomaly.Client.Api.Networking.AnomalyMessaging.FailClosedTransport(
                "The required encrypted-channel receive bridge failed while dispatching a message.");
            SafeLogError(
                $"[EncryptedChannelHandlerFix] bridge dispatch failed: " +
                $"{inner.GetType().Name}: {inner.Message}");
        }
    }
}
