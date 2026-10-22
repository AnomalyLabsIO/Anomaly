using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Anomaly.Bootstrap;
using HarmonyLib;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class CatalogIncrementalWorkTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static object CatalogSync => Field("Sync").GetValue(null)!;

    [Fact]
    public void OrdinaryClassificationAndTrustMissesDoNotResolveTheCatalog()
    {
        lock (CatalogSync)
        {
            var target = Method(nameof(OrdinaryTarget));
            var patch = Method(nameof(Patch));
            Assert.Equal(ProtectionScope.None, ProtectedMethodCatalog.Classify(target));
            long scans = ProtectedMethodCatalog.AssemblyEnumerationCount;
            long work = ProtectedMethodCatalog.ResolverWorkCount;
            for (int i = 0; i < 2000; i++)
            {
                Assert.Equal(ProtectionScope.None, ProtectedMethodCatalog.Classify(target));
                Assert.False(ProtectedMethodCatalog.IsTrustedPatch(target, patch, HarmonyPatchType.Prefix));
            }
            Assert.Equal(scans, ProtectedMethodCatalog.AssemblyEnumerationCount);
            Assert.Equal(work, ProtectedMethodCatalog.ResolverWorkCount);
        }
    }

    [Fact]
    public void RelevantStaticAssemblyMissesAlsoUseOnlyCachedClassification()
    {
        lock (CatalogSync)
        {
            var target = Method(nameof(OrdinaryTarget));
            Assert.Equal(ProtectionScope.None, ProtectedMethodCatalog.Classify(target));
            var relevant = (HashSet<string>)Field("RelevantAssemblyNames").GetValue(null)!;
            string name = target.Module.Assembly.GetName().Name!;
            bool added = relevant.Add(name);
            try
            {
                long work = ProtectedMethodCatalog.ResolverWorkCount;
                long scans = ProtectedMethodCatalog.AssemblyEnumerationCount;
                for (int i = 0; i < 2000; i++)
                    Assert.Equal(ProtectionScope.None, ProtectedMethodCatalog.Classify(target));
                Assert.Equal(work, ProtectedMethodCatalog.ResolverWorkCount);
                Assert.Equal(scans, ProtectedMethodCatalog.AssemblyEnumerationCount);
            }
            finally { if (added) relevant.Remove(name); }
        }
    }

    [Fact]
    public void ExplicitCatalogCheckpointsTakeOnlyOneDomainSnapshot()
    {
        lock (CatalogSync)
        {
            ProtectedMethodCatalog.ResolveLoadedAssemblies();
            long scans = ProtectedMethodCatalog.AssemblyEnumerationCount;
            Assert.Equal(1, scans);
            ProtectedMethodCatalog.ResolveLoadedAssemblies();
            ProtectedMethodCatalog.ResolveLoadedAssemblies();
            Assert.Equal(scans, ProtectedMethodCatalog.AssemblyEnumerationCount);
        }
    }

    [Fact]
    public void MembershipAndTrustRevisionsTrackNewExactEntries()
    {
        lock (CatalogSync)
        {
            var target = Method(nameof(RegisteredTarget));
            var patch = Method(nameof(Patch));
            long revision = ProtectedMethodCatalog.Revision;
            var external = (HashSet<MethodBase>)Field("External").GetValue(null)!;
            int count = external.Count;
            ProtectedMethodCatalog.RegisterExternalTarget(target);
            Assert.True(ProtectedMethodCatalog.Revision > revision);
            Assert.Equal(ProtectionScope.External, ProtectedMethodCatalog.Classify(target));
            Assert.Equal(count + 1, external.Count);
            revision = ProtectedMethodCatalog.Revision;
            ProtectedMethodCatalog.RegisterExternalTarget(target);
            Assert.True(ProtectedMethodCatalog.Revision >= revision);
            Assert.Equal(count + 1, external.Count);
            revision = ProtectedMethodCatalog.Revision;
            ProtectedMethodCatalog.RegisterInfrastructureTarget(target);
            Assert.True(ProtectedMethodCatalog.Revision > revision);
            Assert.Equal(ProtectionScope.Infrastructure, ProtectedMethodCatalog.Classify(target));

            long trust = ProtectedMethodCatalog.TrustRevision;
            ProtectedMethodCatalog.RegisterTrustedPatch(target, patch, HarmonyPatchType.Prefix);
            Assert.Equal(trust + 1, ProtectedMethodCatalog.TrustRevision);
            ProtectedMethodCatalog.RegisterTrustedPatch(target, patch, HarmonyPatchType.Prefix);
            Assert.Equal(trust + 1, ProtectedMethodCatalog.TrustRevision);
            Assert.True(ProtectedMethodCatalog.IsTrustedPatch(target, patch, HarmonyPatchType.Prefix));
            Assert.False(ProtectedMethodCatalog.IsTrustedPatch(target, patch, HarmonyPatchType.Postfix));
        }
    }

    [Fact]
    public void NativeRevisionTracksNewMappingsAndStrongerSourceIdentity()
    {
        lock (CatalogSync)
        {
            var external = Method(nameof(ExternalNativeTarget));
            var infrastructure = Method(nameof(InfrastructureNativeTarget));
            IntPtr address = new(0x20203040);
            ProtectedMethodCatalog.RegisterExternalTarget(external);
            ProtectedMethodCatalog.RegisterInfrastructureTarget(infrastructure);
            long revision = ProtectedMethodCatalog.NativeRevision;
            ProtectedMethodCatalog.RegisterCompiledNativeTarget(external, address);
            Assert.Equal(revision + 1, ProtectedMethodCatalog.NativeRevision);
            ProtectedMethodCatalog.RegisterCompiledNativeTarget(external, address);
            Assert.Equal(revision + 1, ProtectedMethodCatalog.NativeRevision);
            ProtectedMethodCatalog.RegisterCompiledNativeTarget(infrastructure, address);
            Assert.Equal(revision + 2, ProtectedMethodCatalog.NativeRevision);
            Assert.True(ProtectedMethodCatalog.TryGetNativeTarget(address, out MethodBase actual));
            Assert.Equal(infrastructure, actual);
        }
    }

    [Fact]
    public void ObservedGeneratedEntryCompletesOnlyItsOwnNativeJob()
    {
        lock (CatalogSync)
        {
            var generated = new DynamicMethod("incremental-native-wrapper", typeof(void), Type.EmptyTypes);
            generated.GetILGenerator().Emit(OpCodes.Ret);
            MethodInfo target = generated.CreateDelegate(typeof(Action)).Method;
            long enumerations = ProtectedMethodCatalog.CoreEnumerationCount;
            ProtectedMethodCatalog.RegisterInfrastructureTarget(target);
            var queued = (HashSet<MethodBase>)Field("NativeQueued").GetValue(null)!;
            Assert.Contains(target, queued);
            ProtectedMethodCatalog.RegisterCompiledNativeTarget(target, new IntPtr(0x20203050));
            Assert.DoesNotContain(target, queued);
            Assert.Equal(enumerations, ProtectedMethodCatalog.CoreEnumerationCount);
        }
    }

    [Fact]
    public void AssemblyLoadNotificationDoesNotTakeTheCatalogLock()
    {
        lock (CatalogSync)
        {
            ProtectedMethodCatalog.ResolveLoadedAssemblies();
            Exception? failure = null;
            var worker = new Thread(() =>
            {
                try { AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Catalog.Notification." + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run); }
                catch (Exception exception) { failure = exception; }
            }) { IsBackground = true };
            worker.Start();
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "AssemblyLoad must enqueue without taking the catalog lock.");
            Assert.Null(failure);
            Assert.True(ProtectedMethodCatalog.HasPendingWork);
        }
    }

    [Fact]
    public void WorkerCannotDrainQueuedCatalogOrNativeWork()
    {
        lock (CatalogSync)
        {
            ProtectedMethodCatalog.RegisterExternalTarget(Method(nameof(WorkerTarget)));
            long work = ProtectedMethodCatalog.ResolverWorkCount;
            long enumerations = ProtectedMethodCatalog.CoreEnumerationCount;
            bool processed = true;
            var worker = new Thread(() => processed = ProtectedMethodCatalog.ProcessOnePendingWork()) { IsBackground = true };
            worker.Start();
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "The worker should reject work before taking the catalog lock.");
            Assert.False(processed);
            Assert.Equal(work, ProtectedMethodCatalog.ResolverWorkCount);
            Assert.Equal(enumerations, ProtectedMethodCatalog.CoreEnumerationCount);
        }
    }

    [Fact]
    public void BothRegistrationPathsQueueWorkWithoutReadingTargetMetadata()
    {
        lock (CatalogSync)
        {
            var target = new ThrowingMetadataMethodInfo(Method(nameof(OrdinaryTarget)));
            try
            {
                ProtectedMethodCatalog.RegisterExternalTarget(target);
                ProtectedMethodCatalog.RegisterInfrastructureTarget(target);
                ProtectedMethodCatalog.RegisterExternalTarget(target);
                ProtectedMethodCatalog.RegisterInfrastructureTarget(target);
                Assert.Equal(0, target.MetadataReads);
                Assert.Contains(target, (HashSet<MethodBase>)Field("NativeQueued").GetValue(null)!);
                Assert.Contains(target, (HashSet<MethodBase>)Field("BindingQueued").GetValue(null)!);
            }
            finally
            {
                // Stale queue entries validate current membership before reading
                // metadata, so this synthetic target cannot affect later tests.
                foreach (string name in new[] { "External", "Infrastructure", "NativeQueued", "BindingQueued" })
                    ((HashSet<MethodBase>)Field(name).GetValue(null)!).Remove(target);
            }
        }
    }

    private static FieldInfo Field(string name) => typeof(ProtectedMethodCatalog).GetField(name, PrivateStatic)!;
    private static MethodInfo Method(string name) => typeof(CatalogIncrementalWorkTests).GetMethod(name, PrivateStatic)!;
    private static void OrdinaryTarget() { }
    private static void RegisteredTarget() { }
    private static void ExternalNativeTarget() { }
    private static void InfrastructureNativeTarget() { }
    private static void WorkerTarget() { }
    private static void Patch() { }

    private sealed class ThrowingMetadataMethodInfo : MethodInfo
    {
        private readonly MethodInfo _inner;
        internal int MetadataReads;
        internal ThrowingMetadataMethodInfo(MethodInfo inner) => _inner = inner;
        public override string Name { get { MetadataReads++; throw new InvalidOperationException("Registration must not read metadata."); } }
        public override Type? DeclaringType { get { MetadataReads++; throw new InvalidOperationException("Registration must not read metadata."); } }
        public override RuntimeMethodHandle MethodHandle { get { MetadataReads++; throw new InvalidOperationException("Registration must not resolve native code."); } }
        public override Type? ReflectedType => _inner.ReflectedType;
        public override Module Module => _inner.Module;
        public override MethodAttributes Attributes => _inner.Attributes;
        public override Type ReturnType => _inner.ReturnType;
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => _inner.ReturnTypeCustomAttributes;
        public override MethodInfo GetBaseDefinition() => this;
        public override MethodImplAttributes GetMethodImplementationFlags() => _inner.GetMethodImplementationFlags();
        public override ParameterInfo[] GetParameters() => _inner.GetParameters();
        public override object[] GetCustomAttributes(bool inherit) => _inner.GetCustomAttributes(inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => _inner.GetCustomAttributes(attributeType, inherit);
        public override bool IsDefined(Type attributeType, bool inherit) => _inner.IsDefined(attributeType, inherit);
        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture) => _inner.Invoke(obj, invokeAttr, binder, parameters, culture);
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}
