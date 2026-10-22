using System.Collections.Concurrent;
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
    private const int MaximumCheckpointWorkItems = 1000000;
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
    private static readonly HashSet<MethodBase> Infrastructure = new();
    private static readonly HashSet<(MethodBase Target, MethodInfo Patch, HarmonyPatchType Kind)> Trusted = new();
    private static readonly Dictionary<IntPtr, MethodBase> NativeTargets = new();
    private static readonly HashSet<MethodBase> NativeResolved = new();
    private static readonly HashSet<MethodBase> Il2CppNativeResolved = new();
    private static readonly HashSet<string> PermanentGaps = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Type> RequiredTypes = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ResolvedSpecs = new(StringComparer.Ordinal);
    private static readonly HashSet<MethodBase> RequiredRuntimeMethods = new();
    private static readonly Dictionary<Assembly, string?> ObservedAssemblies = new(ReferenceEqualityComparer.Instance);
    private static readonly ConcurrentQueue<Assembly> AssemblyObservations = new();
    private static readonly Queue<Action> CatalogWork = new();
    private static readonly Queue<MethodBase> BindingWork = new();
    private static readonly HashSet<MethodBase> BindingQueued = new();
    private static readonly Dictionary<MethodBase, (string TypeName, string MethodName)?> BindingDescriptors = new();
    private static readonly Dictionary<MethodBase, (string TypeName, string MethodName)> UnresolvedBindings = new();
    private static readonly HashSet<Assembly> UnresolvedAssemblies = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<Assembly> EnumeratedCoreAssemblies = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<MethodBase> CoreNativeMethods = new();
    private static readonly Queue<MethodBase> NativeWork = new();
    private static readonly HashSet<MethodBase> NativeQueued = new();
    private static long _revision;
    private static long _trustRevision;
    private static long _nativeRevision;
    private static long _assemblyEnumerationCount;
    private static long _resolverWorkCount;
    private static long _coreEnumerationCount;
    private static long _nextUnresolvedRetry;
    private static int _nativeInFlight;
    private static int _catalogInFlight;
    private static bool _observingAssemblies;
    private static bool _platformResolved;
    private static bool _loaderResolved;
    private static int _mainThreadId;
    private static bool _nativeReady;
    private static bool _nativeMappingCompleted;
    private static bool _nativeRefreshRequired = true;
    [ThreadStatic] private static bool _resolving;
    [ThreadStatic] private static bool _resolvingNative;

    internal static long Revision { get { lock (Sync) return _revision; } }
    internal static long TrustRevision { get { lock (Sync) return _trustRevision; } }
    internal static long NativeRevision { get { lock (Sync) return _nativeRevision; } }
    internal static (long Catalog, long Trust, long Native) CaptureRevisions()
    {
        lock (Sync) return (_revision, _trustRevision, _nativeRevision);
    }
    internal static long AssemblyEnumerationCount { get { lock (Sync) return _assemblyEnumerationCount; } }
    internal static long ResolverWorkCount { get { lock (Sync) return _resolverWorkCount; } }
    internal static long CoreEnumerationCount { get { lock (Sync) return _coreEnumerationCount; } }
    internal static bool IsMainThread => _mainThreadId != 0 && Environment.CurrentManagedThreadId == _mainThreadId;
    internal static bool IsSupportReady { get { lock (Sync) return _nativeReady; } }
    internal static bool HasPendingWork
    {
        get
        {
            lock (Sync)
                return !AssemblyObservations.IsEmpty || CatalogWork.Count != 0 || _catalogInFlight != 0 || _nativeInFlight != 0 || BindingWork.Count != 0 ||
                    (_nativeReady && NativeWork.Count != 0) ||
                    ((UnresolvedAssemblies.Count != 0 || UnresolvedBindings.Count != 0) && Environment.TickCount64 >= _nextUnresolvedRetry);
        }
    }

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

    private static readonly HashSet<(string Assembly, string Type)> RequiredCandidateTypes = new(
        RequiredMethods.Select(spec => (spec.AssemblyName, spec.TypeName)).Concat(RequiredTypeGroups));

    internal static bool RequiredCoverageComplete => GetCoverageGaps().Length == 0;

    internal static string[] GetCoverageGaps()
    {
        lock (Sync)
        {
            var gaps = new List<string>(PermanentGaps);
            if (!_nativeMappingCompleted || _nativeRefreshRequired || !AssemblyObservations.IsEmpty)
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
                if (!_observingAssemblies)
                {
                    _mainThreadId = Environment.CurrentManagedThreadId;
                    _observingAssemblies = true;
                    // Subscribe before the sole domain snapshot; observations racing
                    // the snapshot are deduplicated by actual assembly identity.
                    AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                    _assemblyEnumerationCount++;
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                        ObserveAssemblyLocked(assembly);
                    ResolvePlatform();
                    ResolveLoader();
                }
                ScheduleUnresolvedRetriesLocked();
            }
            int processed = 0;
            while (ProcessOneCatalogWork())
            {
                if (++processed < MaximumCheckpointWorkItems) continue;
                ReportCheckpointLimit("Assembly catalog checkpoint exceeded its work limit.");
                break;
            }
        }
        catch (Exception exception)
        {
            lock (Sync) AddGapLocked("Catalog resolution failed: " + exception.GetType().Name);
        }
        finally { _resolving = false; }
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        // Never take catalog/guard locks from a CLR loader notification. A
        // reflector holding Sync can be waiting for that loader thread.
        AssemblyObservations.Enqueue(args.LoadedAssembly);
        Interlocked.Increment(ref _revision);
    }

    private static void AddGapLocked(string gap)
    {
        if (PermanentGaps.Add(gap)) Interlocked.Increment(ref _revision);
    }

    private static void ReportCheckpointLimit(string gap)
    {
        lock (Sync) AddGapLocked(gap);
        RuntimeGuard.ReportCoverageGap(gap);
    }

    private static string? ObserveAssemblyLocked(Assembly assembly)
    {
        if (ObservedAssemblies.TryGetValue(assembly, out string? observed))
            return observed;
        GuardTimingDiagnostics.CountTotal(GuardTimingTotal.ResolverAssemblies);
        string? name = assembly.GetName().Name;
        ObservedAssemblies.Add(assembly, name);
        if (name == null || !RelevantAssemblyNames.Contains(name))
            return name;
        if (PinnedAssemblies.TryGetValue(name, out Assembly? previous))
        {
            if (!ReferenceEquals(previous, assembly))
                AddGapLocked("Duplicate protected assembly identity: " + name);
            return name;
        }
        PinnedAssemblies.Add(name, assembly);
        Interlocked.Increment(ref _revision);
        if (CoreAssemblyNames.Contains(name))
        {
            CoreAssemblies.Add(assembly);
            CatalogWork.Enqueue(() => EnumerateCoreAssembly(assembly));
        }
        CatalogWork.Enqueue(() => ResolveAssemblyMetadataWithRetry(assembly, name));
        _nativeRefreshRequired = true;
        if (name == "Anomaly.Client")
            foreach (MethodBase target in UnresolvedBindings.Keys)
                QueueBindingLocked(target);
        return name;
    }

    private static void ScheduleUnresolvedRetriesLocked()
    {
        if (Environment.TickCount64 < _nextUnresolvedRetry)
            return;
        _nextUnresolvedRetry = Environment.TickCount64 + 1000;
        foreach (Assembly assembly in UnresolvedAssemblies)
        {
            string name = ObservedAssemblies[assembly]!;
            CatalogWork.Enqueue(() => ResolveAssemblyMetadataWithRetry(assembly, name));
        }
        foreach (MethodBase target in UnresolvedBindings.Keys)
            QueueBindingLocked(target);
    }

    private static void ResolveAssemblyMetadata(Assembly assembly, string name, Type? candidate = null)
    {
        lock (Sync)
        {
            _resolverWorkCount++;
            foreach (MethodSpec spec in RequiredMethods)
            {
                if (spec.AssemblyName != name || ResolvedSpecs.Contains(spec.Key) ||
                    candidate != null && candidate.FullName != spec.TypeName)
                    continue;
                Type? type = candidate ?? assembly.GetType(spec.TypeName, false);
                MethodBase? method = type == null ? null : ResolveExactMethod(type, spec.Name, spec.Parameters.ToArray());
                if (method == null) continue;
                AddExternalTargetLocked(method);
                RequiredRuntimeMethods.Add(method);
                ResolvedSpecs.Add(spec.Key);
            }
            foreach (var group in RequiredTypeGroups)
            {
                string key = group.Assembly + ":" + group.Type;
                if (group.Assembly != name || RequiredTypes.ContainsKey(key) ||
                    candidate != null && candidate.FullName != group.Type)
                    continue;
                Type? type = candidate ?? assembly.GetType(group.Type, false);
                if (type == null) continue;
                ProtectType(type);
                RequiredTypes.Add(key, type);
            }
            bool unresolved = RequiredMethods.Any(spec => spec.AssemblyName == name && !ResolvedSpecs.Contains(spec.Key)) ||
                RequiredTypeGroups.Any(group => group.Assembly == name && !RequiredTypes.ContainsKey(group.Assembly + ":" + group.Type));
            if (unresolved) UnresolvedAssemblies.Add(assembly);
            else UnresolvedAssemblies.Remove(assembly);
        }
    }

    private static void ResolveAssemblyMetadataWithRetry(Assembly assembly, string name)
    {
        try { ResolveAssemblyMetadata(assembly, name); }
        catch
        {
            lock (Sync) UnresolvedAssemblies.Add(assembly);
            throw;
        }
    }

    private static bool ProcessOneCatalogWork()
    {
        Assembly? observation = null;
        Action? work = null;
        MethodBase? binding = null;
        lock (Sync)
        {
            if (AssemblyObservations.TryDequeue(out observation))
                _catalogInFlight++;
            else if (CatalogWork.Count != 0)
            {
                work = CatalogWork.Dequeue();
                _catalogInFlight++;
            }
            else if (BindingWork.Count != 0)
            {
                binding = BindingWork.Dequeue();
                if (!BindingQueued.Remove(binding)) return true;
                _catalogInFlight++;
            }
        }
        if (observation == null && work == null && binding == null) return false;
        try
        {
            if (observation != null) { lock (Sync) ObserveAssemblyLocked(observation); }
            else if (work != null) work();
            else ResolveClientBinding(binding!);
        }
        catch (Exception exception)
        {
            lock (Sync) AddGapLocked("Catalog resolution failed: " + exception.GetType().Name);
        }
        finally
        {
            lock (Sync)
            {
                _catalogInFlight--;
                UpdateNativeCoverageLocked();
            }
        }
        return true;
    }

    internal static bool ProcessOnePendingWork()
    {
        if (!IsMainThread || _resolvingNative) return false;
        lock (Sync) ScheduleUnresolvedRetriesLocked();
        if (ProcessOneCatalogWork()) return true;
        return ProcessOneNativeWork();
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
            Assembly assembly = target.Module.Assembly;
            string? assemblyName = ObserveAssemblyLocked(assembly);
            if (assemblyName == null || !RelevantAssemblyNames.Contains(assemblyName)) return ProtectionScope.None;
            if (CoreAssemblyNames.Contains(assemblyName)) return ProtectionScope.Core;
            // Static assemblies cannot acquire methods after load. Dynamic type
            // completion has no AssemblyLoad event, so resolve only a matching
            // required declaring type before authorizing its first patch.
            Type? candidate = target.DeclaringType;
            string? candidateName = candidate?.FullName;
            if (candidate == null || candidateName == null || !RequiredCandidateTypes.Contains((assemblyName, candidateName)) ||
                !RequiredMethods.Any(spec => spec.AssemblyName == assemblyName && spec.TypeName == candidate.FullName && !ResolvedSpecs.Contains(spec.Key)) &&
                !RequiredTypeGroups.Any(group => group.Assembly == assemblyName && group.Type == candidate.FullName && !RequiredTypes.ContainsKey(group.Assembly + ":" + group.Type)))
                return ProtectionScope.None;
            using var classificationTiming = GuardTimingDiagnostics.Begin(GuardTimingPhase.CatalogCandidateClassification);
            GuardTimingDiagnostics.CountTotal(GuardTimingTotal.CandidateClassifications);
            try
            {
                ResolveAssemblyMetadata(assembly, assemblyName, candidate);
                if (External.Contains(target)) return ProtectionScope.External;
            }
            catch (Exception exception)
            {
                UnresolvedAssemblies.Add(assembly);
                AddGapLocked("Catalog resolution failed: " + exception.GetType().Name);
                RuntimeGuard.ReportCoverageGap("A first-patch protected target could not be classified: " + exception.GetType().Name);
            }
        }
        return ProtectionScope.None;
    }

    internal static void RegisterInfrastructureTarget(MethodBase target)
    {
        lock (Sync) AddInfrastructureTargetLocked(target);
    }

    internal static void RegisterExternalTarget(MethodBase target)
    {
        lock (Sync) AddExternalTargetLocked(target);
    }

    internal static void RegisterTrustedPatch(MethodBase target, MethodInfo patch, HarmonyPatchType kind)
    {
        lock (Sync)
            if (Trusted.Add((target, patch, kind))) _trustRevision++;
    }

    internal static bool IsTrustedPatch(MethodBase target, MethodInfo patch, HarmonyPatchType kind)
    {
        lock (Sync)
        {
            if (Trusted.Contains((target, patch, kind))) return true;
            // Only a newly registered candidate can need a synchronous binding
            // before its first patch. Ordinary trust misses never resolve again.
            if (BindingQueued.Remove(target)) ResolveClientBinding(target);
            return Trusted.Contains((target, patch, kind));
        }
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
            {
                NativeResolved.Add(method);
                NativeQueued.Remove(method);
                UpdateNativeCoverageLocked();
            }
        }
    }

    internal static bool NativeCoverageReady
    {
        get { lock (Sync) return _nativeMappingCompleted && !_nativeRefreshRequired && AssemblyObservations.IsEmpty; }
    }

    internal static bool RefreshNativeTargetsIfReady()
    {
        lock (Sync)
            if (!_nativeReady || Environment.CurrentManagedThreadId != _mainThreadId)
                return _nativeMappingCompleted && !_nativeRefreshRequired && AssemblyObservations.IsEmpty;
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
        }
        int processed = 0;
        while (ProcessOnePendingWork())
        {
            if (++processed < MaximumCheckpointWorkItems) continue;
            ReportCheckpointLimit("Native catalog checkpoint exceeded its work limit.");
            break;
        }
        lock (Sync) UpdateNativeCoverageLocked();
    }

    private static void ResolvePlatform()
    {
        if (_platformResolved)
            return;
        _platformResolved = true;
        foreach (Type type in new[] { typeof(BinaryWriter), typeof(BinaryReader), typeof(MemoryStream), typeof(Encoding), typeof(UTF8Encoding), typeof(HashAlgorithm), typeof(SHA256), typeof(HMACSHA256), typeof(CustomAttributeData), typeof(Assembly), typeof(Core).Assembly.GetType() })
            ProtectType(type);
        Type? rawAttributes = typeof(CustomAttributeData).Assembly.GetType("System.Reflection.RuntimeCustomAttributeData", false);
        if (rawAttributes == null) AddGapLocked("Runtime raw metadata implementation is unresolved.");
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
            if (prefix == null) AddGapLocked("Loader PatchShield interceptor missing: " + method.Name);
            else RegisterTrustedPatch(method, prefix, HarmonyPatchType.Prefix);
        }
        Type? functions = typeof(HarmonyLib.Harmony).Assembly.GetType("HarmonyLib.PatchFunctions");
        MethodInfo? reverse = functions?.GetMethod("ReversePatch", AllDeclared);
        MethodInfo? reversePrefix = shield.GetMethod("PatchMethod_PatchFunctions_ReversePatch", AllDeclared);
        if (reverse == null || reversePrefix == null) AddGapLocked("Loader PatchShield reverse-patch interceptor missing.");
        else RegisterTrustedPatch(reverse, reversePrefix, HarmonyPatchType.Prefix);

        // MelonLoader redirects byte-array/LoadFile loads into its default ALC.
        // These are already installed before plugins and are part of the known
        // inventory-loading path, not third-party modifications to admit by owner.
        Type? loadFix = shield.Assembly.GetType("MelonLoader.Fixes.Dotnet.DotnetAssemblyLoadContextFix", false);
        if (loadFix == null)
        {
            AddGapLocked("Loader assembly-context fix type is missing.");
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
            AddGapLocked("A required loader assembly-context patch signature is missing.");
        else
            RegisterTrustedPatch(target, prefix, HarmonyPatchType.Prefix);
    }

    private static void ProtectType(Type type)
    {
        foreach (MethodBase method in type.GetMethods(AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AllDeclared)))
            if (!method.IsAbstract && !method.ContainsGenericParameters)
                _nativeRefreshRequired |= AddExternalTargetLocked(method);
    }

    // All insertion paths hold Sync. Registration queues detached work without
    // reading method metadata or entering the JIT/runtime.
    private static bool AddExternalTargetLocked(MethodBase target)
    {
        bool added = External.Add(target);
        if (added)
        {
            Interlocked.Increment(ref _revision);
            QueueBindingLocked(target);
            QueueNativeLocked(target);
            RuntimeGuard.EnqueueTargetAudit(target);
        }
        return added;
    }

    private static void AddInfrastructureTargetLocked(MethodBase target)
    {
        if (!Infrastructure.Add(target)) return;
        Interlocked.Increment(ref _revision);
        QueueNativeLocked(target);
        RuntimeGuard.EnqueueTargetAudit(target);
    }

    private static void QueueBindingLocked(MethodBase target)
    {
        if (BindingQueued.Add(target)) BindingWork.Enqueue(target);
    }

    private static void QueueNativeLocked(MethodBase target)
    {
        if (NativeQueued.Add(target)) NativeWork.Enqueue(target);
        _nativeRefreshRequired = true;
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
                AddInfrastructureTargetLocked(method);
        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            ProtectInfrastructureTree(nested);
    }

    private static void ResolveClientBinding(MethodBase target)
    {
        lock (Sync)
        {
            if (!External.Contains(target)) return;
            if (!BindingDescriptors.TryGetValue(target, out var descriptor))
                BindingDescriptors.Add(target, descriptor = GetClientPatchDescriptor(target));
            if (!descriptor.HasValue) return;
            UnresolvedBindings[target] = descriptor.Value;
            if (!PinnedAssemblies.TryGetValue("Anomaly.Client", out Assembly? client)) return;
            MethodInfo? patch = client.GetType(descriptor.Value.TypeName, false)?.GetMethod(descriptor.Value.MethodName, AllDeclared);
            if (patch == null) return;
            RegisterTrustedPatch(target, patch, HarmonyPatchType.Prefix);
            UnresolvedBindings.Remove(target);
        }
    }

    private static void EnumerateCoreAssembly(Assembly assembly)
    {
        lock (Sync)
        {
            if (!EnumeratedCoreAssemblies.Add(assembly)) return;
            _coreEnumerationCount++;
        }
        try
        {
            foreach (Type type in assembly.GetTypes())
            {
                Type candidate = type;
                lock (Sync) CatalogWork.Enqueue(() => EnumerateCoreType(candidate));
            }
        }
        catch (Exception exception)
        {
            lock (Sync) AddGapLocked("Core native target catalog incomplete: " + assembly.GetName().Name + ": " + exception.GetType().Name);
        }
    }

    private static void EnumerateCoreType(Type type)
    {
        foreach (MethodBase method in type.GetMethods(AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AllDeclared)))
        {
            if (method.IsAbstract || method.ContainsGenericParameters) continue;
            lock (Sync)
            {
                if (!CoreNativeMethods.Add(method)) continue;
                QueueNativeLocked(method);
                RuntimeGuard.EnqueueTargetAudit(method);
            }
        }
    }

    private static void UpdateNativeCoverageLocked()
    {
        _nativeRefreshRequired = NativeQueued.Count != 0 || _nativeInFlight != 0 || _catalogInFlight != 0 || CatalogWork.Count != 0 || !AssemblyObservations.IsEmpty;
        _nativeMappingCompleted = _nativeReady && !_nativeRefreshRequired;
    }

    private static bool ProcessOneNativeWork()
    {
        MethodBase method;
        lock (Sync)
        {
            if (!_nativeReady || !IsMainThread || NativeWork.Count == 0) return false;
            method = NativeWork.Dequeue();
            if (!NativeQueued.Remove(method)) return true;
            _nativeInFlight++;
        }
        _resolvingNative = true;
        try
        {
            lock (Sync)
                if (!Infrastructure.Contains(method) && !External.Contains(method) && !CoreNativeMethods.Contains(method))
                    return true;
            if (!CanResolveNativeTarget(method)) return true;
            bool resolveManaged;
            bool resolveIl2Cpp;
            lock (Sync)
            {
                resolveManaged = NativeResolved.Add(method);
                resolveIl2Cpp = method.DeclaringType?.FullName?.StartsWith("Il2Cpp", StringComparison.Ordinal) == true && Il2CppNativeResolved.Add(method);
            }
            if (resolveManaged)
            {
                // Runtime/JIT work never holds the catalog lock: MonoMod can
                // synchronously notify a worker owning a detour chain lock.
                IntPtr precode = IsDynamicNativeTarget(method) ? IntPtr.Zero : method.MethodHandle.GetFunctionPointer();
                IntPtr compiled = method.GetNativeStart();
                lock (Sync)
                {
                    AddNativeTarget(precode, method);
                    AddNativeTarget(compiled, method);
                }
            }
            if (!resolveIl2Cpp) return true;
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
                    AddGapLocked("Required IL2CPP native entry unresolved: " + method.DeclaringType?.FullName + "." + method.Name);
        }
        catch (Exception exception)
        {
            lock (Sync) AddGapLocked("Native target unresolved: " + method.DeclaringType?.FullName + "." + method.Name + ": " + exception.GetType().Name);
        }
        finally
        {
            _resolvingNative = false;
            lock (Sync)
            {
                _nativeInFlight--;
                UpdateNativeCoverageLocked();
            }
        }
        return true;
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
        {
            if (previous == method) return;
            NativeTargets[pointer] = method;
            _nativeRevision++;
        }
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
