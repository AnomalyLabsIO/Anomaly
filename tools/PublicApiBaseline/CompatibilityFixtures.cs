using System.Globalization;
using System.Runtime.CompilerServices;
using Mono.Cecil;

internal static class CompatibilityFixtures
{
    internal static int Run()
    {
        var cases = new (string Name, Action<TypeDefinition> Mutate)[]
        {
            ("sealed type", t => t.IsSealed = true),
            ("abstract type", t => t.IsAbstract = true),
            ("constant value", t => t.Fields[0].Constant = 2),
            ("field visibility", t => t.Fields[0].Attributes = FieldAttributes.Family | FieldAttributes.Static | FieldAttributes.Literal),
            ("method visibility", t => t.Methods[0].Attributes = MethodAttributes.Family),
            ("parameter name", t => t.Methods[0].Parameters[0].Name = "renamed"),
            ("optional flag", t => t.Methods[0].Parameters[0].IsOptional = false),
            ("default value", t => t.Methods[0].Parameters[0].Constant = 2),
            ("default removed", t => t.Methods[0].Parameters[0].HasConstant = false),
            ("out parameter", t => t.Methods[0].Parameters[0].IsOut = true),
            ("nested protected surface", t => t.NestedTypes[0].IsNestedPrivate = true),
            ("enum value", t => t.Module.Types.Single(x => x.Name == "Choice").Fields[1].Constant = 7),
            ("decimal field value", t => ChangeAttributeArgument(t.Fields.Single(f => f.Name == "Amount").CustomAttributes[0], 4, 54321u)),
            ("decimal field scale", t => ChangeAttributeArgument(t.Fields.Single(f => f.Name == "Amount").CustomAttributes[0], 0, (byte)3)),
            ("decimal field sign", t => ChangeAttributeArgument(t.Fields.Single(f => f.Name == "Amount").CustomAttributes[0], 1, (byte)0)),
            ("decimal parameter default", t => ChangeAttributeArgument(t.Methods[0].Parameters[1].CustomAttributes[0], 4, 54321u)),
            ("decimal default removed", t => t.Methods[0].Parameters[1].CustomAttributes.Clear()),
            ("DateTime field value", t => ChangeAttributeArgument(t.Fields.Single(f => f.Name == "Timestamp").CustomAttributes[0], 0, 638000000000000001L)),
            ("DateTime field attribute removed", t => t.Fields.Single(f => f.Name == "Timestamp").CustomAttributes.Clear()),
            ("DateTime parameter default", t => ChangeAttributeArgument(t.Methods[0].Parameters[2].CustomAttributes[0], 0, 638000000000000001L)),
        };
        foreach (var test in cases)
        {
            using var assembly = CreateFixture();
            var before = PublicApiSurface.Generate(assembly);
            test.Mutate(assembly.MainModule.Types.Single(t => t.Name == "Contract"));
            if (before == PublicApiSurface.Generate(assembly))
                throw new InvalidOperationException("Undetected contract change: " + test.Name);
        }

        using var stable = CreateFixture();
        var expected = PublicApiSurface.Generate(stable);
        stable.MainModule.Types.Single(t => t.Name == "Contract").Methods.Add(
            new MethodDefinition("Implementation", MethodAttributes.Private, stable.MainModule.TypeSystem.Void));
        if (expected != PublicApiSurface.Generate(stable))
            throw new InvalidOperationException("A private implementation change altered the contract.");
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            if (expected != PublicApiSurface.Generate(stable))
                throw new InvalidOperationException("Baseline depends on the current culture.");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        if (expected.Split('\n').Count(l => l.StartsWith("F|", StringComparison.Ordinal)) != 6)
            throw new InvalidOperationException("Escaped string constant injected a baseline record.");
        Console.WriteLine($"Public API compatibility fixtures passed ({cases.Length + 3} checks).");
        return 0;
    }

    private static AssemblyDefinition CreateFixture()
    {
        var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Fixture", new Version(1, 0)), "Fixture", ModuleKind.Dll);
        var module = assembly.MainModule;
        var type = new TypeDefinition("Fixture", "Contract", TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(type);
        type.Fields.Add(new FieldDefinition("Number", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal, module.TypeSystem.Int32) { Constant = 1 });
        type.Fields.Add(new FieldDefinition("Text", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal, module.TypeSystem.String) { Constant = "a|b;c:\nF|injection" });
        type.Fields.Add(new FieldDefinition("Fraction", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal, module.TypeSystem.Double) { Constant = 1.25 });
        var amount = new FieldDefinition("Amount", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly, module.ImportReference(typeof(decimal)));
        amount.CustomAttributes.Add(DecimalConstant(module));
        type.Fields.Add(amount);
        var timestamp = new FieldDefinition("Timestamp", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly, module.ImportReference(typeof(DateTime)));
        timestamp.CustomAttributes.Add(DateTimeConstant(module));
        type.Fields.Add(timestamp);
        var method = new MethodDefinition("Call", MethodAttributes.Public, module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.Optional | ParameterAttributes.HasDefault, module.TypeSystem.Int32) { Constant = 1 });
        var amountParameter = new ParameterDefinition("amount", ParameterAttributes.Optional, amount.FieldType);
        amountParameter.CustomAttributes.Add(DecimalConstant(module));
        method.Parameters.Add(amountParameter);
        var timestampParameter = new ParameterDefinition("timestamp", ParameterAttributes.Optional, timestamp.FieldType);
        timestampParameter.CustomAttributes.Add(DateTimeConstant(module));
        method.Parameters.Add(timestampParameter);
        type.Methods.Add(method);
        type.NestedTypes.Add(new TypeDefinition("", "Extension", TypeAttributes.NestedFamily, module.TypeSystem.Object));
        var enumeration = new TypeDefinition("Fixture", "Choice", TypeAttributes.Public | TypeAttributes.Sealed, module.ImportReference(typeof(Enum)));
        enumeration.Fields.Add(new FieldDefinition("value__", FieldAttributes.Private | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, module.TypeSystem.Int32));
        enumeration.Fields.Add(new FieldDefinition("First", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal, enumeration) { Constant = 0 });
        module.Types.Add(enumeration);
        return assembly;
    }

    private static CustomAttribute DecimalConstant(ModuleDefinition module)
    {
        var attribute = new CustomAttribute(module.ImportReference(typeof(DecimalConstantAttribute)
            .GetConstructor(new[] { typeof(byte), typeof(byte), typeof(uint), typeof(uint), typeof(uint) })!));
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.Byte, (byte)2));
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.Byte, (byte)128));
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.UInt32, 0u));
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.UInt32, 0u));
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.UInt32, 12345u));
        return attribute;
    }

    private static CustomAttribute DateTimeConstant(ModuleDefinition module)
    {
        var attribute = new CustomAttribute(module.ImportReference(typeof(DateTimeConstantAttribute)
            .GetConstructor(new[] { typeof(long) })!));
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.Int64, 638000000000000000L));
        return attribute;
    }

    private static void ChangeAttributeArgument(CustomAttribute attribute, int index, object value) =>
        attribute.ConstructorArguments[index] = new CustomAttributeArgument(attribute.ConstructorArguments[index].Type, value);
}
