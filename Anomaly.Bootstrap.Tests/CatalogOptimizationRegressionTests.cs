using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class CatalogOptimizationRegressionTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    [Fact]
    public void SignatureKeyCannotChangeAfterCallerParameterArrayMutation()
    {
        string[] input = { "System.Byte[]", "System.Int32", "System.Int32" };
        var specification = new ProtectedMethodCatalog.MethodSpec("Fixture", "Writer", "Write", input);
        string key = specification.Key;
        input[0] = "System.String";
        Assert.Equal("Fixture:Writer.Write(System.Byte[],System.Int32,System.Int32)", key);
        Assert.Equal(key, specification.Key);
        Assert.Equal("System.Byte[]", specification.Parameters[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)specification.Parameters)[0] = "System.String");
        Assert.Equal(key, specification.Key);
    }

    [Fact]
    public void SimilarlyCasedAssemblyNamesCannotGainCriticalClassification()
    {
        // These distinct, noncritical assembly names never pin a required identity
        // or contaminate the existing first-Assembly-CSharp catalog test.
        foreach (string name in new[] { "anomaly.client", "ANOMALY.CLIENT", "assembly-csharp", "bouncycastle.cryptography" })
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
            var type = assembly.DefineDynamicModule("ordinal-fixture").DefineType("Fixture.NonCritical", TypeAttributes.Public);
            var method = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            method.GetILGenerator().Emit(OpCodes.Ret);
            Assert.Equal(ProtectionScope.None, ProtectedMethodCatalog.Classify(type.CreateType()!.GetMethod("Run")!));
        }
        foreach (string fieldName in new[] { "CoreAssemblyNames", "RelevantAssemblyNames" })
        {
            var names = (HashSet<string>)Field(fieldName).GetValue(null)!;
            Assert.Same(StringComparer.Ordinal, names.Comparer);
        }
    }

    [Fact]
    public void DirectRegistrationDoesNotReadOrThrowFromTargetMetadata()
    {
        var target = new ThrowingMetadataMethodInfo(typeof(CatalogOptimizationRegressionTests).GetMethod(nameof(Fixture), PrivateStatic)!);
        object sync = Field("Sync").GetValue(null)!;
        lock (sync)
        {
            var external = (HashSet<MethodBase>)Field("External").GetValue(null)!;
            bool previousNativeRefresh = (bool)Field("_nativeRefreshRequired").GetValue(null)!;
            try
            {
                ProtectedMethodCatalog.RegisterExternalTarget(target);
                Assert.Contains(target, external);
                Assert.Equal(0, target.MetadataReads);
                // A duplicate registration must also avoid metadata observation.
                ProtectedMethodCatalog.RegisterExternalTarget(target);
                Assert.Equal(0, target.MetadataReads);
            }
            finally
            {
                // Remove only this synthetic target and its queued memberships.
                // No protected assembly, trusted tuple or native mapping changes.
                external.Remove(target);
                ((HashSet<MethodBase>)Field("NativeQueued").GetValue(null)!).Remove(target);
                ((HashSet<MethodBase>)Field("BindingQueued").GetValue(null)!).Remove(target);
                Field("_nativeRefreshRequired").SetValue(null, previousNativeRefresh);
            }
        }
    }

    private static FieldInfo Field(string name) => typeof(ProtectedMethodCatalog).GetField(name, PrivateStatic)!;
    private static void Fixture() { }

    private sealed class ThrowingMetadataMethodInfo : MethodInfo
    {
        private readonly MethodInfo _real;
        internal ThrowingMetadataMethodInfo(MethodInfo real) => _real = real;
        internal int MetadataReads;
        public override Type? DeclaringType { get { ++MetadataReads; throw new InvalidOperationException("Metadata must be observed only during resolution."); } }
        public override string Name { get { ++MetadataReads; throw new InvalidOperationException("Metadata must be observed only during resolution."); } }
        public override Type? ReflectedType => _real.ReflectedType;
        public override Module Module => _real.Module;
        public override RuntimeMethodHandle MethodHandle => _real.MethodHandle;
        public override MethodAttributes Attributes => _real.Attributes;
        public override Type ReturnType => _real.ReturnType;
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => _real.ReturnTypeCustomAttributes;
        public override MethodInfo GetBaseDefinition() => this;
        public override MethodImplAttributes GetMethodImplementationFlags() => _real.GetMethodImplementationFlags();
        public override ParameterInfo[] GetParameters() => _real.GetParameters();
        public override object[] GetCustomAttributes(bool inherit) => _real.GetCustomAttributes(inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => _real.GetCustomAttributes(attributeType, inherit);
        public override bool IsDefined(Type attributeType, bool inherit) => _real.IsDefined(attributeType, inherit);
        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, System.Globalization.CultureInfo? culture) => _real.Invoke(obj, invokeAttr, binder, parameters, culture);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}
