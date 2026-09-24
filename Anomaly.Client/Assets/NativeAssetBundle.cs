extern alias UnityCore;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;
using Il2CppGCHandle = Il2CppSystem.Runtime.InteropServices.GCHandle;
using ManagedSpanWrapper = UnityCore::UnityEngine.Bindings.ManagedSpanWrapper;
using UnityObject = UnityEngine.Object;

namespace Anomaly.Client.Assets;

/// <summary>
/// Native bundle-loading bridge for the specific SCP:SL Unity profile below.
/// Call on the Unity main thread. Callers own content validation and bundle lifetime;
/// this adapter does not make an untrusted bundle safe to load.
/// </summary>
internal static class NativeAssetBundle
{
    internal const string SupportedUnityVersion = "6000.0.43f1";
    internal static long LoadAttempts { get; private set; }

    // AssetBundle itself has been stripped from this player's IL2CPP metadata.
    // Its restored managed facade supplies injected calls but cannot be cast or
    // constructed from an IL2CPP pointer. Retain the real Unity Object instead.
    internal sealed class Handle
    {
        private readonly Dictionary<int, (UnityObject Asset, HideFlags OriginalFlags)> _loadedAssets = new();
        internal UnityObject Owner { get; private set; }

        internal Handle(UnityObject owner) => Owner = owner;

        internal void Retain(UnityObject asset)
        {
            if (asset == null || _loadedAssets.ContainsKey(asset.GetInstanceID())) return;
            HideFlags flags = asset.hideFlags;
            asset.hideFlags = flags | HideFlags.DontUnloadUnusedAsset;
            _loadedAssets.Add(asset.GetInstanceID(), (asset, flags));
        }

        internal void Invalidate()
        {
            Owner = null;
            foreach (var entry in _loadedAssets.Values)
                if (entry.Asset != null) entry.Asset.hideFlags = entry.OriginalFlags;
            _loadedAssets.Clear();
        }
    }

    /// <summary>Loads a local bundle, returning null when Unity rejects its contents.</summary>
    internal static unsafe Handle LoadFromFile(string path, uint crc = 0)
    {
        ValidateName(path, nameof(path));
        EnsureSupportedProfile();
        string fullPath = Path.GetFullPath(path);

        // The generated Unity 6 string overload enters an unavailable IL2CPP
        // ReadOnlySpan<char>.GetPinnableReference. Pin the managed UTF-16 string
        // only for the synchronous injected call, preserving Unity's own ABI.
        IntPtr handle;
        LoadAttempts++;
        fixed (char* characters = fullPath)
        {
            var span = new ManagedSpanWrapper(characters, fullPath.Length);
            handle = AssetBundle.LoadFromFile_Internal_Injected(ref span, crc, 0);
        }

        // Unity 6 returns a GC handle here, not an IL2CPP object pointer.
        var owner = UnmarshalObject(handle);
        return ReferenceEquals(owner, null) ? null : new Handle(owner);
    }

    /// <summary>
    /// Loads a named asset, keeping it alive across Resources.UnloadUnusedAssets
    /// while this handle is loaded. Managed wrapper references alone do not do so.
    /// </summary>
    internal static unsafe T LoadAsset<T>(Handle bundle, string assetName) where T : UnityObject
    {
        ValidateName(assetName, nameof(assetName));
        EnsureSupportedProfile();
        IntPtr bundlePointer = GetBundlePointer(bundle);
        var assetType = Il2CppType.Of<T>();

        try
        {
            IntPtr handle;
            fixed (char* characters = assetName)
            {
                var span = new ManagedSpanWrapper(characters, assetName.Length);
                handle = AssetBundle.LoadAsset_Internal_Injected(bundlePointer, ref span, assetType);
            }

            var asset = UnmarshalObject(handle)?.TryCast<T>();
            bundle.Retain(asset);
            return asset;
        }
        finally
        {
            GC.KeepAlive(bundle);
        }
    }

    /// <summary>
    /// Unloads the bundle and releases its asset-retention flags. With false,
    /// surviving assets become the caller's responsibility; true destroys them.
    /// </summary>
    internal static void Unload(Handle bundle, bool unloadAllLoadedObjects)
    {
        EnsureSupportedProfile();
        IntPtr bundlePointer = GetBundlePointer(bundle);
        var owner = bundle.Owner;
        try
        {
            AssetBundle.Unload_Injected(bundlePointer, unloadAllLoadedObjects);
            bundle.Invalidate();
        }
        finally
        {
            GC.KeepAlive(owner);
        }
    }

    private static IntPtr GetBundlePointer(Handle bundle)
    {
        if (ReferenceEquals(bundle, null))
            throw new ArgumentNullException(nameof(bundle));

        // The generated generic MarshalNotNull<AssetBundle> can be stripped too.
        // Bundles passed to this adapter must still be loaded; use Unity's cached
        // C++ pointer and refuse unloaded objects instead of resurrecting them.
        IntPtr pointer = ReferenceEquals(bundle.Owner, null) ? IntPtr.Zero : bundle.Owner.m_CachedPtr;
        if (pointer == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(bundle), "The Unity AssetBundle is no longer alive.");
        return pointer;
    }

    private static UnityObject UnmarshalObject(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return null;

        // Match Unity 6000.0 BindingsHelpers.UnmarshalUnityObject without its
        // stripped IL2CPP generic metadata. Live handles remain owned by Unity;
        // only Unity's temporary fake-null wrapper (instance ID zero) is freed.
        var nativeHandle = Il2CppGCHandle.FromIntPtr(handle);
        var asset = nativeHandle.Target?.Cast<UnityObject>();
        if (ReferenceEquals(asset, null))
            return null;

        if (asset.GetInstanceID() == 0)
        {
            nativeHandle.Free();
            return null;
        }
        return asset;
    }

    private static void ValidateName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0)
            throw new ArgumentException("The value must be nonempty and must not contain a null character.", parameterName);
    }

    private static void EnsureSupportedProfile()
    {
        string version = Application.unityVersion;
        RuntimePlatform platform = Application.platform;
        Architecture architecture = RuntimeInformation.ProcessArchitecture;
        if (!string.Equals(version, SupportedUnityVersion, StringComparison.Ordinal) ||
            platform != RuntimePlatform.WindowsPlayer || architecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                $"Native AssetBundle loading requires Unity {SupportedUnityVersion} WindowsPlayer x64; " +
                $"the current profile is Unity {version}, {platform}, {architecture}.");
        }
    }
}
