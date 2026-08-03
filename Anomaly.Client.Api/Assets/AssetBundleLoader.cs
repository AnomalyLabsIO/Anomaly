extern alias UnityCoreModule;

using System;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Runtime;
using UnityEngine;
using ManagedSpanWrapper = UnityCoreModule::UnityEngine.Bindings.ManagedSpanWrapper;
using Unmarshal = UnityCoreModule::UnityEngine.Bindings.Unmarshal;

namespace Anomaly.Client.Api.Assets
{
    /// <summary>
    /// IL2CPP-safe access to Unity asset bundles.
    /// </summary>
    /// <remarks>
    /// Unity 6000's generated <see cref="AssetBundle"/> wrappers marshal strings
    /// through <c>Il2CppSystem.ReadOnlySpan&lt;char&gt;</c> and unmarshal returned
    /// Unity objects through a generated generic method. Those two generated
    /// paths are not callable in the SCP:SL 14.2.7 interop runtime. This facade
    /// uses the same public injected bindings while supplying the span and
    /// GC-handle conversion explicitly.
    /// </remarks>
    public static unsafe class AssetBundleLoader
    {
        public static AssetBundle LoadFromFile(string path, uint crc = 0, ulong offset = 0)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("An asset-bundle path is required.", nameof(path));

            fixed (char* pathPointer = path)
            {
                var pathSpan = new ManagedSpanWrapper(pathPointer, path.Length);
                IntPtr handle = AssetBundle.LoadFromFile_Internal_Injected(
                    ref pathSpan,
                    crc,
                    offset);
                return UnmarshalAssetBundle(handle);
            }
        }

        public static T LoadAsset<T>(AssetBundle bundle, string assetName)
            where T : UnityEngine.Object
        {
            if (bundle == null)
                throw new ArgumentNullException(nameof(bundle));
            if (string.IsNullOrEmpty(assetName))
                throw new ArgumentException("An asset name is required.", nameof(assetName));

            Il2CppSystem.Type assetType = Il2CppSystem.Type.GetTypeFromHandle(
                RuntimeReflectionHelper.GetRuntimeTypeHandle<T>());
            fixed (char* namePointer = assetName)
            {
                var nameSpan = new ManagedSpanWrapper(namePointer, assetName.Length);
                IntPtr handle = AssetBundle.LoadAsset_Internal_Injected(
                    bundle.Pointer,
                    ref nameSpan,
                    assetType);
                return UnmarshalUnityObject<T>(handle);
            }
        }

        private static T UnmarshalUnityObject<T>(IntPtr gcHandlePointer)
            where T : UnityEngine.Object
        {
            if (gcHandlePointer == IntPtr.Zero)
                return null;

            Il2CppSystem.Runtime.InteropServices.GCHandle handle =
                Unmarshal.FromIntPtrUnsafe(gcHandlePointer);
            Il2CppSystem.Object target = handle.Target;
            if (target == null)
                return null;

            T result = Il2CppWrapperFactory<T>.Create(target.Pointer);
            if ((object)result != null && result.GetInstanceID() != 0)
                return result;

            handle.Free();
            return null;
        }

        private static AssetBundle UnmarshalAssetBundle(IntPtr gcHandlePointer)
        {
            if (gcHandlePointer == IntPtr.Zero)
                return null;

            Il2CppSystem.Runtime.InteropServices.GCHandle handle =
                Unmarshal.FromIntPtrUnsafe(gcHandlePointer);
            Il2CppSystem.Object target = handle.Target;
            if (target == null)
                return null;

            AssetBundle result = Il2CppWrapperFactory<AssetBundle>.Create(target.Pointer);
            if ((object)result != null && result.GetInstanceID() != 0)
                return result;

            handle.Free();
            return null;
        }

        /// <summary>
        /// Creates the managed wrapper without consulting
        /// <c>Il2CppClassPointerStore&lt;T&gt;.NativeClassPtr</c>.
        /// </summary>
        /// <remarks>
        /// Unity's generated <see cref="AssetBundle"/> wrapper has no
        /// <see cref="IntPtr"/> constructor and its class-pointer store is not
        /// initialized by this interop build. Il2CppInterop itself handles that
        /// case in the internal <c>Il2CppObjectBase.InitializerStore&lt;T&gt;</c>
        /// factory. Resolve that exact factory once per type instead of using
        /// <c>TryCast</c>, which rejects the otherwise valid wrapper.
        /// </remarks>
        private static class Il2CppWrapperFactory<T>
            where T : Il2CppObjectBase
        {
            public static readonly Func<IntPtr, T> Create = Resolve();

            private static Func<IntPtr, T> Resolve()
            {
                Type openStore = typeof(Il2CppObjectBase).GetNestedType(
                    "InitializerStore`1",
                    BindingFlags.NonPublic);
                if (openStore == null)
                    throw new MissingMemberException(
                        typeof(Il2CppObjectBase).FullName,
                        "InitializerStore`1");

                Type closedStore = openStore.MakeGenericType(typeof(T));
                PropertyInfo initializer = closedStore.GetProperty(
                    "Initializer",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (initializer?.GetValue(null) is not Func<IntPtr, T> factory)
                    throw new MissingMemberException(
                        closedStore.FullName,
                        "Initializer");

                return factory;
            }
        }
    }
}
