using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using MelonLoader;
using MonoMod.RuntimeDetour;

namespace Anomaly.Bootstrap;

internal enum ProtectionScope { None, Core, External, Infrastructure }

internal static class ProtectedMethodCatalog
{
    private const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private const string Bytes = "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>";
    private const string Endpoint = "Il2CppSystem.Net.IPEndPoint";
    private const string Writer = "Il2CppLiteNetLib.Utils.NetDataWriter";
    private const string Packet = "Il2CppLiteNetLib.NetPacket";
    private static readonly object Sync = new();
    private static readonly string[] CoreNames = { "Anomaly.Bootstrap", "Anomaly.Client", "Anomaly.Client.Api", "Anomaly.Shared" };
    private static readonly HashSet<string> CoreAssemblyNames = new(CoreNames, StringComparer.Ordinal);
    private static readonly Dictionary<string, Assembly> PinnedAssemblies = new(StringComparer.Ordinal);
    private static readonly HashSet<Assembly> CoreAssemblies = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<MethodBase> External = new();
    private static MethodBase[] _externalSnapshot = Array.Empty<MethodBase>();
    private static bool _externalSnapshotDirty = true;
    private static readonly HashSet<MethodBase> Infrastructure = new();
    private static readonly HashSet<(MethodBase Target, MethodInfo Patch, HarmonyPatchType Kind)> Trusted = new();
    private static readonly Dictionary<IntPtr, MethodBase> NativeTargets = new();
    private static readonly HashSet<MethodBase> NativeResolved = new();
    private static readonly HashSet<MethodBase> Il2CppNativeResolved = new();
    private static readonly HashSet<string> PermanentGaps = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Type> RequiredTypes = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ResolvedSpecs = new(StringComparer.Ordinal);
    private static readonly HashSet<MethodBase> RequiredRuntimeMethods = new();
    private static bool _platformResolved;
    private static bool _loaderResolved;
    private static int _mainThreadId;
    private static bool _nativeReady;
    private static bool _nativeMappingCompleted;
    private static bool _nativeRefreshRequired = true;
    [ThreadStatic] private static bool _resolving;
    [ThreadStatic] private static bool _resolvingNative;

    // Methods are resolved against the actual loaded assembly and exact parameter
    // types. A similarly named overload never satisfies a required catalog slot.
    internal sealed class MethodSpec
    {
        internal string AssemblyName { get; }
        internal string TypeName { get; }
        internal string Name { get; }
        internal IReadOnlyList<string> Parameters { get; }
        internal string Key { get; }

        internal MethodSpec(string assembly, string type, string name, params string[] parameters)
        {
            AssemblyName = assembly;
            TypeName = type;
            Name = name;
            Parameters = Array.AsReadOnly(parameters.ToArray());
            Key = AssemblyName + ":" + TypeName + "." + Name + "(" + string.Join(",", Parameters) + ")";
        }
    }

    private static readonly MethodSpec[] RequiredMethods =
    {
        new("Assembly-CSharp", "Il2Cpp.CustomLiteNetLib4MirrorTransport", "GetConnectData", Writer),
        new("Assembly-CSharp", "Il2Cpp.CustomLiteNetLib4MirrorTransport", "OnConncetionRefused", "Il2CppLiteNetLib.DisconnectInfo"),
        new("Assembly-CSharp", "Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport", "GetConnectData", Writer),
        new("Assembly-CSharp", "Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport", "ClientConnect", "System.String"),
        new("Assembly-CSharp", "Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport", "ClientDisconnect"),
        new("Assembly-CSharp", "Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorClient", "ConnectClient", Writer),
        new("Assembly-CSharp", "Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorClient", "OnPeerDisconnected", "Il2CppLiteNetLib.NetPeer", "Il2CppLiteNetLib.DisconnectInfo"),
        new("Assembly-CSharp", "Il2CppLiteNetLib.Utils.NetDataWriter", "PutBytesWithLength", Bytes),
        new("Assembly-CSharp", "Il2CppLiteNetLib.Utils.NetDataWriter", "PutBytesWithLength", Bytes, "System.Int32", "System.UInt16"),
        new("Assembly-CSharp", "Il2CppLiteNetLib.Utils.NetDataWriter", "Put", "System.String", "System.Int32"),
        new("Assembly-CSharp", "Il2CppLiteNetLib.Utils.NetDataWriter", "Put", "System.Byte"),
        new("Assembly-CSharp", "Il2CppLiteNetLib.Utils.NetDataWriter", "Put", Bytes),
        new("Assembly-CSharp", "Il2CppLiteNetLib.Utils.NetDataWriter", "Put", Bytes, "System.Int32", "System.Int32"),
        new("Assembly-CSharp", "Il2CppLiteNetLib.Utils.NetDataWriter", "CopyData"),
        new("Assembly-CSharp", "Il2CppLiteNetLib.Utils.NetDataWriter", "get_Data"),
        new("Assembly-CSharp", "Il2CppLiteNetLib.Utils.NetDataWriter", "get_Length"),
        new("Assembly-CSharp", "Il2CppLiteNetLib.NetManager", "Connect", "System.String", "System.Int32", Writer),
        new("Assembly-CSharp", "Il2CppLiteNetLib.NetManager", "Connect", Endpoint, Writer),
        new("Assembly-CSharp", "Il2CppLiteNetLib.LiteNetManager", "Connect", "System.String", "System.Int32", Writer),
        new("Assembly-CSharp", "Il2CppLiteNetLib.LiteNetManager", "Connect", Endpoint, Writer),
        new("Assembly-CSharp", "Il2CppLiteNetLib.LiteNetManager", "SendRaw", Packet, Endpoint),
        new("Assembly-CSharp", "Il2CppLiteNetLib.LiteNetManager", "SendRaw", Bytes, "System.Int32", "System.Int32", Endpoint),
        new("Assembly-CSharp", "Il2CppLiteNetLib.LiteNetManager", "SendRawAndRecycle", Packet, Endpoint),
        new("Assembly-CSharp", "Il2CppLiteNetLib.LiteNetManager", "SendRawCore", Bytes, "System.Int32", "System.Int32", Endpoint),
        new("Assembly-CSharp", "Il2CppLiteNetLib.LiteNetManager", "SendRawCoreWithCleanup", Bytes, "System.Int32", "System.Int32", Endpoint, Packet),
        new("BouncyCastle.Cryptography", "Org.BouncyCastle.Security.SignerUtilities", "GetSigner", "System.String"),
        new("BouncyCastle.Cryptography", "Org.BouncyCastle.Security.CipherUtilities", "GetCipher", "System.String"),
        new("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Signers.DsaDigestSigner", "BlockUpdate", "System.Byte[]", "System.Int32", "System.Int32"),
        new("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Signers.DsaDigestSigner", "GenerateSignature"),
        new("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Signers.DsaDigestSigner", "VerifySignature", "System.Byte[]"),
        new("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Agreement.X25519Agreement", "CalculateAgreement", "Org.BouncyCastle.Crypto.ICipherParameters", "System.Byte[]", "System.Int32"),
    };

    private static readonly HashSet<string> RelevantAssemblyNames = new(
        CoreNames.Concat(RequiredMethods.Select(spec => spec.AssemblyName)), StringComparer.Ordinal);

    internal static IReadOnlyList<MethodSpec> RequiredMethodSpecifications => Array.AsReadOnly(RequiredMethods);

    // Concrete factory results and their serialization/crypto implementation
    // classes are part of the catalog, not just their public factory methods.
    private static readonly (string Assembly, string Type)[] RequiredTypeGroups =
    {
        ("Assembly-CSharp", Writer),
        ("Assembly-CSharp", "Il2CppLiteNetLib.Utils.NetDataReader"),
        ("Assembly-CSharp", "Il2CppLiteNetLib.NetConnectRequestPacket"),
        ("Assembly-CSharp", Packet),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Security.SignerUtilities"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Security.CipherUtilities"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Signers.DsaDigestSigner"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Signers.ECDsaSigner"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Signers.StandardDsaEncoding"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Signers.RandomDsaKCalculator"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Modes.GcmBlockCipher"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Modes.Gcm.Tables4kGcmMultiplier"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Modes.Gcm.GcmUtilities"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.BufferedAeadBlockCipher"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.BufferedCipherBase"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.AesUtilities"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Engines.AesEngine"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Digests.Sha256Digest"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Digests.GeneralDigest"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Agreement.X25519Agreement"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Generators.X25519KeyPairGenerator"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Parameters.X25519PrivateKeyParameters"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Parameters.X25519PublicKeyParameters"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Math.EC.Rfc7748.X25519"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Math.EC.Rfc7748.X25519Field"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Parameters.AeadParameters"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Crypto.Parameters.KeyParameter"),
        ("BouncyCastle.Cryptography", "Org.BouncyCastle.Security.SecureRandom"),
    };

    internal static IReadOnlyList<(string Assembly, string Type)> RequiredTypeSpecifications => Array.AsReadOnly(RequiredTypeGroups);

    internal static bool RequiredCoverageComplete => GetCoverageGaps().Length == 0;

    internal static string[] GetCoverageGaps()
    {
        lock (Sync)
        {
            var gaps = new List<string>(PermanentGaps);
            if (!_nativeMappingCompleted || _nativeRefreshRequired)
                gaps.Add("Native target catalog has not completed the required support-ready mapping phase.");
            foreach (var name in CoreNames)
                if (!PinnedAssemblies.ContainsKey(name)) gaps.Add("Required Anomaly assembly is not loaded: " + name);
            foreach (var spec in RequiredMethods)
                if (!ResolvedSpecs.Contains(spec.Key)) gaps.Add("Required target unresolved: " + spec.Key);
            foreach (var group in RequiredTypeGroups)
                if (!RequiredTypes.ContainsKey(group.Assembly + ":" + group.Type)) gaps.Add("Required implementation type unresolved: " + group.Type);
            return gaps.ToArray();
        }
    }

    internal static void ResolveLoadedAssemblies()
    {
        if (_resolving)
            return;
        _resolving = true;
        using var resolveTiming = GuardTimingDiagnostics.Begin(GuardTimingPhase.CatalogResolveLoaded);
        GuardTimingDiagnostics.CountTotal(GuardTimingTotal.ResolverCalls);
        try
        {
            lock (Sync)
            {
                if (_mainThreadId == 0)
                    _mainThreadId = Environment.CurrentManagedThreadId;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    GuardTimingDiagnostics.CountTotal(GuardTimingTotal.ResolverAssemblies);
                    string? name = assembly.GetName().Name;
                    if (name == null || !RelevantAssemblyNames.Contains(name))
                        continue;
                    if (PinnedAssemblies.TryGetValue(name, out var previous))
                    {
                        if (!ReferenceEquals(previous, assembly))
                            PermanentGaps.Add("Duplicate protected assembly identity: " + name);
                        continue;
                    }
                    PinnedAssemblies.Add(name, assembly);
                    _nativeRefreshRequired = true;
                    if (CoreAssemblyNames.Contains(name))
                        CoreAssemblies.Add(assembly);
                }
                ResolvePlatform();
                ResolveLoader();
                foreach (var spec in RequiredMethods)
                {
                    if (ResolvedSpecs.Contains(spec.Key) || !PinnedAssemblies.TryGetValue(spec.AssemblyName, out var assembly))
                        continue;
                    Type? type = assembly.GetType(spec.TypeName, false);
                    MethodBase? method = type == null ? null : ResolveExactMethod(type, spec.Name, spec.Parameters.ToArray());
                    if (method == null)
                        continue;
                    _nativeRefreshRequired |= AddExternalTargetLocked(method);
                    RequiredRuntimeMethods.Add(method);
                    ResolvedSpecs.Add(spec.Key);
                }
                foreach (var group in RequiredTypeGroups)
                {
                    string key = group.Assembly + ":" + group.Type;
                    if (RequiredTypes.ContainsKey(key) || !PinnedAssemblies.TryGetValue(group.Assembly, out var assembly))
                        continue;
                    Type? type = assembly.GetType(group.Type, false);
                    if (type == null)
                        continue;
                    ProtectType(type);
                    RequiredTypes.Add(key, type);
                }
                ResolveClientPatches();
            }
        }
        catch (Exception exception)
        {
            lock (Sync) PermanentGaps.Add("Catalog resolution failed: " + exception.GetType().Name);
        }
        finally { _resolving = false; }
    }

    internal static MethodBase? ResolveExactMethod(Type type, string name, params string[] parameterNames)
    {
        IEnumerable<MethodBase> candidates = name == ".ctor" ? type.GetConstructors(AllDeclared) : type.GetMethods(AllDeclared).Where(method => method.Name == name);
        return candidates.SingleOrDefault(method => !method.ContainsGenericParameters &&
            method.GetParameters().Select(parameter => TypeName(parameter.ParameterType)).SequenceEqual(parameterNames, StringComparer.Ordinal));
    }

    internal static string TypeName(Type type)
    {
        if (type.IsByRef) return TypeName(type.GetElementType()!) + "&";
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericType) return type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
        return type.FullName ?? type.Name;
    }

    internal static ProtectionScope Classify(MethodBase target)
    {
        lock (Sync)
        {
            if (Infrastructure.Contains(target)) return ProtectionScope.Infrastructure;
            if (CoreAssemblies.Contains(target.Module.Assembly)) return ProtectionScope.Core;
            if (External.Contains(target)) return ProtectionScope.External;
        }
        // The first patch can arrive immediately after AssemblyLoad. Resolve a
        // relevant new assembly BEFORE returning None to the policy interceptor.
        string? assemblyName = target.Module.Assembly.GetName().Name;
        if (assemblyName != null && RelevantAssemblyNames.Contains(assemblyName))
        {
            using var classificationTiming = GuardTimingDiagnostics.Begin(GuardTimingPhase.CatalogCandidateClassification);
            GuardTimingDiagnostics.CountTotal(GuardTimingTotal.CandidateClassifications);
            ResolveLoadedAssemblies();
            lock (Sync)
            {
                if (Infrastructure.Contains(target)) return ProtectionScope.Infrastructure;
                if (CoreAssemblyNames.Contains(assemblyName)) return ProtectionScope.Core;
                if (External.Contains(target)) return ProtectionScope.External;
            }
        }
        return ProtectionScope.None;
    }

    internal static void RegisterInfrastructureTarget(MethodBase target)
    {
        lock (Sync) _nativeRefreshRequired |= Infrastructure.Add(target);
    }

    internal static void RegisterExternalTarget(MethodBase target)
    {
        lock (Sync) _nativeRefreshRequired |= AddExternalTargetLocked(target);
    }

    internal static void RegisterTrustedPatch(MethodBase target, MethodInfo patch, HarmonyPatchType kind)
    {
        lock (Sync) Trusted.Add((target, patch, kind));
    }

    internal static bool IsTrustedPatch(MethodBase target, MethodInfo patch, HarmonyPatchType kind)
    {
        lock (Sync)
            if (Trusted.Contains((target, patch, kind))) return true;
        ResolveLoadedAssemblies();
        lock (Sync) return Trusted.Contains((target, patch, kind));
    }

    internal static bool TryGetNativeTarget(IntPtr pointer, out MethodBase target)
    {
        lock (Sync)
            return NativeTargets.TryGetValue(pointer, out target!);
    }

    internal static void RegisterCompiledNativeTarget(MethodBase method, IntPtr address)
    {
        if (address == IntPtr.Zero || Classify(method) == ProtectionScope.None)
            return;
        // The guarded MonoMod compile notification supplies the actual new JIT
        // entry. Do not invoke JIT/runtime resolution from its callback thread.
        lock (Sync)
        {
            AddNativeTarget(address, method);
            // Generated detour wrappers have no separate supported MethodHandle
            // precode to discover. Their observed concrete entry is sufficient.
            if (IsDynamicNativeTarget(method))
                NativeResolved.Add(method);
        }
    }

    internal static bool NativeCoverageReady
    {
        get { lock (Sync) return _nativeMappingCompleted && !_nativeRefreshRequired; }
    }

    internal static bool RefreshNativeTargetsIfReady()
    {
        lock (Sync)
            if (!_nativeReady || Environment.CurrentManagedThreadId != _mainThreadId)
                return _nativeMappingCompleted && !_nativeRefreshRequired;
        ResolveNativeTargetsAfterSupportReady();
        return NativeCoverageReady;
    }

    internal static void ResolveNativeTargetsAfterSupportReady()
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            throw new InvalidOperationException("Native catalog resolution requires the Bootstrap main thread.");
        ResolveLoadedAssemblies();
        lock (Sync)
        {
            _nativeReady = true;
            if (_resolvingNative || !_nativeRefreshRequired)
                return;
            _nativeMappingCompleted = false;
            _nativeRefreshRequired = false;
        }
        ResolveNativeTargets();
        lock (Sync) _nativeMappingCompleted = true;
    }

    private static void ResolvePlatform()
    {
        if (_platformResolved)
            return;
        _platformResolved = true;
        foreach (Type type in new[] { typeof(BinaryWriter), typeof(BinaryReader), typeof(MemoryStream), typeof(Encoding), typeof(UTF8Encoding), typeof(HashAlgorithm), typeof(SHA256), typeof(HMACSHA256), typeof(CustomAttributeData), typeof(Assembly), typeof(Core).Assembly.GetType() })
            ProtectType(type);
        Type? rawAttributes = typeof(CustomAttributeData).Assembly.GetType("System.Reflection.RuntimeCustomAttributeData", false);
        if (rawAttributes == null) PermanentGaps.Add("Runtime raw metadata implementation is unresolved.");
        else ProtectType(rawAttributes);
        // Create does not sign or hash application data; it resolves the concrete
        // implementations behind platform factories for this runtime.
        using var sha = SHA256.Create();
        using var hmac = new HMACSHA256();
        ProtectType(sha.GetType());
        ProtectType(hmac.GetType());
        ProtectType(Encoding.UTF8.GetType());
    }

    private static void ResolveLoader()
    {
        if (_loaderResolved)
            return;
        _loaderResolved = true;
        foreach (Type type in new[] { typeof(MelonBase), typeof(MelonMod), typeof(MelonAssembly), typeof(MelonInfoAttribute), typeof(MelonIDAttribute) })
            ProtectType(type);
        // Loader PatchShield's preexisting hooks are required. Trust the actual
        // methods in the loaded MelonLoader assembly, not a Harmony owner string.
        Type shield = typeof(PatchShield);
        ProtectInfrastructureTree(shield);
        foreach (var method in typeof(PatchProcessor).GetMethods(AllDeclared).Where(method => method.Name == "Patch" || method.Name == "Unpatch"))
        {
            MethodInfo? prefix = shield.GetMethod("PatchMethod_PatchProcessor_" + method.Name, AllDeclared);
            if (prefix == null) PermanentGaps.Add("Loader PatchShield interceptor missing: " + method.Name);
            else RegisterTrustedPatch(method, prefix, HarmonyPatchType.Prefix);
        }
        Type? functions = typeof(HarmonyLib.Harmony).Assembly.GetType("HarmonyLib.PatchFunctions");
        MethodInfo? reverse = functions?.GetMethod("ReversePatch", AllDeclared);
        MethodInfo? reversePrefix = shield.GetMethod("PatchMethod_PatchFunctions_ReversePatch", AllDeclared);
        if (reverse == null || reversePrefix == null) PermanentGaps.Add("Loader PatchShield reverse-patch interceptor missing.");
        else RegisterTrustedPatch(reverse, reversePrefix, HarmonyPatchType.Prefix);

        // MelonLoader redirects byte-array/LoadFile loads into its default ALC.
        // These are already installed before plugins and are part of the known
        // inventory-loading path, not third-party modifications to admit by owner.
        Type? loadFix = shield.Assembly.GetType("MelonLoader.Fixes.Dotnet.DotnetAssemblyLoadContextFix", false);
        if (loadFix == null)
        {
            PermanentGaps.Add("Loader assembly-context fix type is missing.");
            return;
        }
        ProtectInfrastructureTree(loadFix);
        RegisterLoaderPatch(typeof(Assembly).GetMethod(nameof(Assembly.Load), new[] { typeof(byte[]), typeof(byte[]) }),
            loadFix.GetMethod("PreAssemblyLoad", AllDeclared, null, new[] { typeof(byte[]), typeof(byte[]), typeof(Assembly).MakeByRefType() }, null));
        RegisterLoaderPatch(typeof(Assembly).GetMethod(nameof(Assembly.LoadFile), new[] { typeof(string) }),
            loadFix.GetMethod("PreAssemblyLoadFile", AllDeclared, null, new[] { typeof(string), typeof(Assembly).MakeByRefType() }, null));
    }

    private static void RegisterLoaderPatch(MethodInfo? target, MethodInfo? prefix)
    {
        if (target == null || prefix == null)
            PermanentGaps.Add("A required loader assembly-context patch signature is missing.");
        else
            RegisterTrustedPatch(target, prefix, HarmonyPatchType.Prefix);
    }

    private static void ProtectType(Type type)
    {
        foreach (MethodBase method in type.GetMethods(AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AllDeclared)))
            if (!method.IsAbstract && !method.ContainsGenericParameters)
                _nativeRefreshRequired |= AddExternalTargetLocked(method);
    }

    // All External insertion paths hold Sync. Registration remains getter-free;
    // only the detached target snapshot is invalidated by a new registration.
    private static bool AddExternalTargetLocked(MethodBase target)
    {
        bool added = External.Add(target);
        if (added)
            _externalSnapshotDirty = true;
        return added;
    }

    private static (string TypeName, string MethodName)? GetClientPatchDescriptor(MethodBase target)
    {
        string? targetType = target.DeclaringType?.FullName;
        if (targetType == "Il2Cpp.CustomLiteNetLib4MirrorTransport" && target.Name == "GetConnectData")
            return ("Anomaly.Client.Patches.Auth.NetDataWriterPut", "OnPrefix1");
        if (targetType == "Il2Cpp.CustomLiteNetLib4MirrorTransport" && target.Name == "OnConncetionRefused")
            return ("Anomaly.Client.Patches.Auth.NetDataWriterPut", "ConnectionRefused");
        if (targetType == "Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorTransport" && target.Name == "ClientDisconnect")
            return ("Anomaly.Client.Patches.Auth.NetDataWriterPut", "ObserveLocalDisconnect");
        if (targetType == "Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorClient" && target.Name == "OnPeerDisconnected")
            return ("Anomaly.Client.Patches.Auth.NetDataWriterPut", "ObservePeerDisconnect");
        if (targetType == "Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorClient" && target.Name == "ConnectClient")
            return ("Anomaly.Client.Patches.Auth.ClientConnectAdmissionGuard", "Prefix");
        if (targetType == "Il2CppLiteNetLib.LiteNetManager" && target.Name is "SendRaw" or "SendRawAndRecycle" or "SendRawCore" or "SendRawCoreWithCleanup")
            return ("Anomaly.Client.Patches.Auth.AdmissionTransportGuard", "Prefix");
        return null;
    }

    private static void ProtectInfrastructureTree(Type type)
    {
        foreach (MethodBase method in type.GetMethods(AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AllDeclared)))
            if (!method.IsAbstract && !method.ContainsGenericParameters)
                _nativeRefreshRequired |= Infrastructure.Add(method);
        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            ProtectInfrastructureTree(nested);
    }

    private static void ResolveClientPatches()
    {
        if (!PinnedAssemblies.TryGetValue("Anomaly.Client", out var client))
            return;
        // Preserve the original detached snapshot and metadata read order. New
        // registrations invalidate it even when reflection reenters this lock.
        // Eligibility and actual client bindings are retried on every resolution.
        if (_externalSnapshotDirty)
        {
            _externalSnapshot = External.ToArray();
            _externalSnapshotDirty = false;
        }
        var snapshot = _externalSnapshot;
        foreach (var target in snapshot)
        {
            var descriptor = GetClientPatchDescriptor(target);
            if (descriptor.HasValue)
                TrustClientPatch(client, target, descriptor.Value.TypeName, descriptor.Value.MethodName);
        }
    }

    private static void TrustClientPatch(Assembly client, MethodBase target, string typeName, string methodName)
    {
        MethodInfo? patch = client.GetType(typeName, false)?.GetMethod(methodName, AllDeclared);
        if (patch != null) Trusted.Add((target, patch, HarmonyPatchType.Prefix));
    }

    private static void ResolveNativeTargets()
    {
        _resolvingNative = true;
        try
        {
            Assembly[] coreAssemblies;
            MethodBase[] catalogMethods;
            lock (Sync)
            {
                coreAssemblies = CoreAssemblies.ToArray();
                catalogMethods = Infrastructure.Concat(External).ToArray();
            }
            var coreMethods = new List<MethodBase>();
            foreach (Assembly assembly in coreAssemblies)
            {
                try
                {
                    foreach (Type type in assembly.GetTypes())
                        coreMethods.AddRange(type.GetMethods(AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AllDeclared))
                            .Where(method => !method.IsAbstract && !method.ContainsGenericParameters));
                }
                catch (Exception exception)
                {
                    lock (Sync) PermanentGaps.Add("Core native target catalog incomplete: " + assembly.GetName().Name + ": " + exception.GetType().Name);
                }
            }
            foreach (MethodBase method in catalogMethods.Concat(coreMethods))
            {
                if (!CanResolveNativeTarget(method))
                    continue;
                try
                {
                    bool resolveManaged;
                    bool resolveIl2Cpp;
                    lock (Sync)
                    {
                        resolveManaged = NativeResolved.Add(method);
                        resolveIl2Cpp = _nativeReady && method.DeclaringType?.FullName?.StartsWith("Il2Cpp", StringComparison.Ordinal) == true && Il2CppNativeResolved.Add(method);
                    }
                    if (resolveManaged)
                    {
                        // Never hold Sync across JIT/runtime operations: MonoMod
                        // callbacks may already own a detour chain lock and need
                        // to consult the catalog from another thread.
                        IntPtr precode = IsDynamicNativeTarget(method) ? IntPtr.Zero : method.MethodHandle.GetFunctionPointer();
                        IntPtr compiled = method.GetNativeStart();
                        lock (Sync)
                        {
                            AddNativeTarget(precode, method);
                            AddNativeTarget(compiled, method);
                        }
                    }
                    if (!resolveIl2Cpp)
                        continue;
                    // Generated wrappers load a NativeMethodInfoPtr field. Read
                    // MethodInfo.methodPointer, not the metadata address itself.
                    FieldInfo? field = FindNativeMethodInfoField(method);
                    bool resolvedNative = false;
                    if (field?.GetValue(null) is IntPtr metadata && metadata != IntPtr.Zero)
                    {
                        IntPtr native = Marshal.ReadIntPtr(metadata);
                        lock (Sync)
                        {
                            AddNativeTarget(native, method);
                            resolvedNative = native != IntPtr.Zero;
                        }
                    }
                    lock (Sync)
                        if (!resolvedNative && RequiredRuntimeMethods.Contains(method))
                            PermanentGaps.Add("Required IL2CPP native entry unresolved: " + method.DeclaringType?.FullName + "." + method.Name);
                }
                catch (Exception exception)
                {
                    lock (Sync) PermanentGaps.Add("Native target unresolved: " + method.DeclaringType?.FullName + "." + method.Name + ": " + exception.GetType().Name);
                }
            }
        }
        finally { _resolvingNative = false; }
    }

    internal static bool CanResolveNativeTarget(MethodBase method)
    {
        if (method.IsAbstract || method.ContainsGenericParameters)
            return false;
        // Open generics have no single callable address. .NET's generated
        // asynchronous delegate stubs are also not invocable on this runtime.
        // Their managed identities remain protected; ordinary methods with
        // these names and the synchronous delegate Invoke are still mapped.
        return !(method.DeclaringType?.IsSubclassOf(typeof(MulticastDelegate)) == true &&
            method.Name is "BeginInvoke" or "EndInvoke" &&
            (method.GetMethodImplementationFlags() & MethodImplAttributes.CodeTypeMask) == MethodImplAttributes.Runtime);
    }

    internal static bool IsDynamicNativeTarget(MethodBase method) =>
        method is DynamicMethod || method.GetType().DeclaringType == typeof(DynamicMethod);

    private static void AddNativeTarget(IntPtr pointer, MethodBase method)
    {
        if (pointer == IntPtr.Zero)
            return;
        if (!NativeTargets.TryGetValue(pointer, out var previous) ||
            Classify(method) is ProtectionScope.Core or ProtectionScope.Infrastructure && Classify(previous) == ProtectionScope.External)
            NativeTargets[pointer] = method;
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);

    internal static FieldInfo? FindNativeMethodInfoField(MethodBase method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null) return null;
        for (int position = 0; position < il.Length;)
        {
            short value = il[position++];
            if (value == 0xfe) value = (short)(0xfe00 | il[position++]);
            OpCode op = OpCodesByValue[value];
            if (op == OpCodes.Ldsfld)
            {
                FieldInfo? field = method.Module.ResolveField(BitConverter.ToInt32(il, position));
                if (field?.FieldType == typeof(IntPtr) && field.Name.StartsWith("NativeMethodInfoPtr_", StringComparison.Ordinal))
                    return field;
            }
            position += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, position),
                _ => 4,
            };
        }
        return null;
    }
}
