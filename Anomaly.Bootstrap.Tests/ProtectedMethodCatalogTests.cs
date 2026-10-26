using System.Reflection;
using System.Reflection.Emit;
using Anomaly.Bootstrap;
using MelonLoader;
using Mono.Cecil;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class ProtectedMethodCatalogTests
{
    [Fact]
    public void CompleteCoverageRequiresTheSupportReadyNativeMappingPhase()
    {
        Assert.False(ProtectedMethodCatalog.NativeCoverageReady);
        Assert.False(ProtectedMethodCatalog.RefreshNativeTargetsIfReady());
        Assert.Contains(ProtectedMethodCatalog.GetCoverageGaps(), gap => gap.Contains("support-ready mapping phase", StringComparison.Ordinal));
    }

    [Fact]
    public void CompileNotificationMapsOnlyProtectedMethodsWithoutNativeResolution()
    {
        MethodInfo target = typeof(Fixture).GetMethod(nameof(Fixture.CompiledEntry))!;
        IntPtr address = new(0x10203040);
        ProtectedMethodCatalog.RegisterCompiledNativeTarget(target, address);
        Assert.False(ProtectedMethodCatalog.TryGetNativeTarget(address, out _));

        ProtectedMethodCatalog.RegisterExternalTarget(target);
        ProtectedMethodCatalog.RegisterCompiledNativeTarget(target, address);
        Assert.True(ProtectedMethodCatalog.TryGetNativeTarget(address, out var actual));
        Assert.Equal(target, actual);
        Assert.False(ProtectedMethodCatalog.NativeCoverageReady);
        ProtectedMethodCatalog.RegisterCompiledNativeTarget(target, IntPtr.Zero);
        Assert.False(ProtectedMethodCatalog.TryGetNativeTarget(IntPtr.Zero, out _));
    }

    [Fact]
    public void GeneratedDetourWrappersUseTheirObservedNativeEntryWithoutAMethodHandle()
    {
        var generated = new DynamicMethod("catalog-generated-wrapper", typeof(void), Type.EmptyTypes);
        generated.GetILGenerator().Emit(OpCodes.Ret);
        MethodInfo runtimeMethod = generated.CreateDelegate(typeof(Action)).Method;
        Assert.True(ProtectedMethodCatalog.IsDynamicNativeTarget(generated));
        Assert.True(ProtectedMethodCatalog.IsDynamicNativeTarget(runtimeMethod));
        Assert.False(ProtectedMethodCatalog.IsDynamicNativeTarget(typeof(Fixture).GetMethod(nameof(Fixture.CompiledEntry))!));

        ProtectedMethodCatalog.RegisterInfrastructureTarget(runtimeMethod);
        IntPtr observed = new(0x10203050);
        ProtectedMethodCatalog.RegisterCompiledNativeTarget(runtimeMethod, observed);
        Assert.True(ProtectedMethodCatalog.TryGetNativeTarget(observed, out var target));
        Assert.Equal(runtimeMethod, target);
        Assert.Equal(ProtectionScope.Infrastructure, ProtectedMethodCatalog.Classify(runtimeMethod));
    }

    [Fact]
    public void LoaderShieldAndGeneratedCallbacksAreMandatoryInfrastructure()
    {
        ProtectedMethodCatalog.ResolveLoadedAssemblies();
        Type shield = typeof(PatchShield);
        Assert.Equal(ProtectionScope.Infrastructure, ProtectedMethodCatalog.Classify(
            shield.GetMethod("MethodCheck", BindingFlags.NonPublic | BindingFlags.Static)!));
        Assert.Equal(ProtectionScope.Infrastructure, ProtectedMethodCatalog.Classify(
            shield.GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Static)!));
        var callbackMethods = shield.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.Name.Contains("b__", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(callbackMethods);
        Assert.All(callbackMethods, method => Assert.Equal(ProtectionScope.Infrastructure, ProtectedMethodCatalog.Classify(method)));
    }

    [Fact]
    public void ExistingLoaderAssemblyContextFixesUseExactTargetPatchAndKindTuples()
    {
        ProtectedMethodCatalog.ResolveLoadedAssemblies();
        Type fixes = typeof(PatchShield).Assembly.GetType("MelonLoader.Fixes.Dotnet.DotnetAssemblyLoadContextFix", true)!;
        MethodInfo load = typeof(Assembly).GetMethod(nameof(Assembly.Load), new[] { typeof(byte[]), typeof(byte[]) })!;
        MethodInfo loadFile = typeof(Assembly).GetMethod(nameof(Assembly.LoadFile), new[] { typeof(string) })!;
        MethodInfo prefix = fixes.GetMethod("PreAssemblyLoad", BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo filePrefix = fixes.GetMethod("PreAssemblyLoadFile", BindingFlags.Public | BindingFlags.Static)!;

        Assert.True(ProtectedMethodCatalog.IsTrustedPatch(load, prefix, HarmonyLib.HarmonyPatchType.Prefix));
        Assert.True(ProtectedMethodCatalog.IsTrustedPatch(loadFile, filePrefix, HarmonyLib.HarmonyPatchType.Prefix));
        Assert.False(ProtectedMethodCatalog.IsTrustedPatch(load, filePrefix, HarmonyLib.HarmonyPatchType.Prefix));
        Assert.False(ProtectedMethodCatalog.IsTrustedPatch(load, prefix, HarmonyLib.HarmonyPatchType.Postfix));
        Assert.Equal(ProtectionScope.Infrastructure, ProtectedMethodCatalog.Classify(prefix));
        Assert.Equal(ProtectionScope.Infrastructure, ProtectedMethodCatalog.Classify(filePrefix));
    }

    [Fact]
    public void FirstPatchTargetInANewCriticalAssemblyIsClassifiedBeforeAnyTrustLookup()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Assembly-CSharp"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("catalog-fixture")
            .DefineType("Il2CppLiteNetLib.Utils.NetDataWriter", System.Reflection.TypeAttributes.Public);
        var method = type.DefineMethod("Put", System.Reflection.MethodAttributes.Public, typeof(void), new[] { typeof(byte) });
        method.GetILGenerator().Emit(OpCodes.Ret);
        MethodInfo target = type.CreateType()!.GetMethod("Put")!;

        Assert.Equal(ProtectionScope.External, ProtectedMethodCatalog.Classify(target));
    }

    [Theory]
    [InlineData("Anomaly.Client.Scripting", "Anomaly.Client.Scripting.Bindings.ScriptBindingRegistry", "AutoDiscoverScriptTypes")]
    [InlineData("Anomaly.MoonSharp.Interpreter", "MoonSharp.Interpreter.Script", "RegisterBindings")]
    public void ScriptRegistrationMethodsRemainProtectedCoreWithoutPatchExemptions(string assemblyName, string typeName, string methodName)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("scripting-catalog-fixture")
            .DefineType(typeName, System.Reflection.TypeAttributes.Public);
        var method = type.DefineMethod(methodName, System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        MethodInfo registration = type.CreateType()!.GetMethod(methodName)!;

        // Registration calls are ordinary managed work, not permission to patch
        // the host or interpreter. All new registration methods inherit core
        // protection from their assembly without a per-member catalog entry.
        Assert.Equal(ProtectionScope.Core, ProtectedMethodCatalog.Classify(registration));
        Assert.False(ProtectedMethodCatalog.IsTrustedPatch(registration, registration, HarmonyLib.HarmonyPatchType.Prefix));
    }

    [Fact]
    public void ResolverRequiresExactOverloadsAndDoesNotFallBackToName()
    {
        MethodBase? expected = ProtectedMethodCatalog.ResolveExactMethod(typeof(Fixture), "Write", "System.Byte[]", "System.Int32", "System.Int32");
        Assert.NotNull(expected);
        Assert.Equal(3, expected.GetParameters().Length);
        Assert.Null(ProtectedMethodCatalog.ResolveExactMethod(typeof(Fixture), "Write", "System.Byte[]", "System.Int64", "System.Int64"));
        Assert.Null(ProtectedMethodCatalog.ResolveExactMethod(typeof(Fixture), "Missing", "System.Byte[]"));
        Assert.NotNull(ProtectedMethodCatalog.ResolveExactMethod(typeof(Fixture), ".ctor", "System.Int32"));
        Assert.Null(ProtectedMethodCatalog.ResolveExactMethod(typeof(Fixture), ".ctor", "System.Int64"));
    }

    [Fact]
    public void GenericAndByReferenceTypeNamesAreUnambiguous()
    {
        Assert.Equal("System.Collections.Generic.List`1<System.Byte>", ProtectedMethodCatalog.TypeName(typeof(List<byte>)));
        Assert.Equal("System.Byte[]&", ProtectedMethodCatalog.TypeName(typeof(byte[]).MakeByRefType()));
    }

    [Fact]
    public void NativeMappingSkipsOnlyOpenGenericsAndRuntimeDelegateAsyncStubs()
    {
        MethodInfo generic = typeof(Fixture).GetMethod(nameof(Fixture.Generic))!;
        Assert.False(ProtectedMethodCatalog.CanResolveNativeTarget(generic));
        Assert.True(ProtectedMethodCatalog.CanResolveNativeTarget(generic.MakeGenericMethod(typeof(int))));
        Assert.False(ProtectedMethodCatalog.CanResolveNativeTarget(typeof(GenericFixture<>).GetMethod(nameof(GenericFixture<int>.Run))!));
        Assert.True(ProtectedMethodCatalog.CanResolveNativeTarget(typeof(GenericFixture<int>).GetMethod(nameof(GenericFixture<int>.Run))!));
        Assert.False(ProtectedMethodCatalog.CanResolveNativeTarget(typeof(FixtureDelegate).GetMethod("BeginInvoke")!));
        Assert.False(ProtectedMethodCatalog.CanResolveNativeTarget(typeof(FixtureDelegate).GetMethod("EndInvoke")!));
        Assert.True(ProtectedMethodCatalog.CanResolveNativeTarget(typeof(FixtureDelegate).GetMethod("Invoke")!));
        Assert.True(ProtectedMethodCatalog.CanResolveNativeTarget(typeof(Fixture).GetMethod(nameof(Fixture.BeginInvoke))!));
        Assert.True(ProtectedMethodCatalog.CanResolveNativeTarget(typeof(Fixture).GetMethod(nameof(Fixture.EndInvoke))!));
    }

    [Fact]
    public void GeneratedWrapperFieldResolutionReadsILWithoutExecutingTheWrapper()
    {
        var method = typeof(Fixture).GetMethod(nameof(Fixture.GeneratedWrapper))!;
        FieldInfo? field = ProtectedMethodCatalog.FindNativeMethodInfoField(method);
        Assert.NotNull(field);
        Assert.Equal("NativeMethodInfoPtr_fixture", field.Name);
        Assert.Null(ProtectedMethodCatalog.FindNativeMethodInfoField(typeof(Fixture).GetMethod(nameof(Fixture.UnrelatedField))!));
    }

    [Fact]
    public void CurrentInstalledDependenciesContainEveryRequiredSignature()
    {
        var metadata = typeof(ProtectedMethodCatalogTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value!);
        var paths = new Dictionary<string, string>
        {
            ["Assembly-CSharp"] = Path.Combine(metadata["AnomalyClientInteropDir"], "Assembly-CSharp.dll"),
            ["BouncyCastle.Cryptography"] = Path.Combine(metadata["AnomalyServerReferenceDir"], "BouncyCastle.Cryptography.dll"),
        };
        foreach (var group in ProtectedMethodCatalog.RequiredMethodSpecifications.GroupBy(spec => spec.AssemblyName))
        {
            Assert.True(File.Exists(paths[group.Key]), "Required configured catalog input is missing: " + paths[group.Key]);
            using var assembly = AssemblyDefinition.ReadAssembly(paths[group.Key]);
            foreach (var spec in group)
            {
                var type = assembly.MainModule.GetType(spec.TypeName);
                Assert.NotNull(type);
                var matching = type.Methods.Where(method => method.Name == spec.Name && !method.HasGenericParameters &&
                    method.Parameters.Select(parameter => CecilTypeName(parameter.ParameterType)).SequenceEqual(spec.Parameters)).ToArray();
                Assert.True(matching.Length == 1, "Required catalog signature missing or ambiguous: " + spec.Key);
            }
            foreach (var type in ProtectedMethodCatalog.RequiredTypeSpecifications.Where(type => type.Assembly == group.Key))
                Assert.True(assembly.MainModule.GetType(type.Type) != null, "Required catalog implementation type missing: " + type.Type);
        }
    }

    private static string CecilTypeName(TypeReference type) => type switch
    {
        GenericInstanceType generic => generic.ElementType.FullName + "<" + string.Join(",", generic.GenericArguments.Select(CecilTypeName)) + ">",
        ByReferenceType byReference => CecilTypeName(byReference.ElementType) + "&",
        ArrayType array => CecilTypeName(array.ElementType) + "[]",
        _ => type.FullName.Replace('/', '+'),
    };

    private sealed class Fixture
    {
        private static IntPtr NativeMethodInfoPtr_fixture = new(1);
        private static IntPtr Other = new(2);
        public Fixture(int count) { }
        public void Write(byte[] value) { }
        public void Write(byte[] value, int offset, int count) { }
        public static IntPtr GeneratedWrapper() => NativeMethodInfoPtr_fixture;
        public static IntPtr UnrelatedField() => Other;
        public static T Generic<T>(T value) => value;
        public static void BeginInvoke() { }
        public static void EndInvoke() { }
        public static void CompiledEntry() { }
    }

    private sealed class GenericFixture<T>
    {
        public void Run() { }
    }

    private delegate void FixtureDelegate();
}
