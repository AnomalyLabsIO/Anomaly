using Mono.Cecil;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ReferencePublicizer <stock-assembly> <output-reference>");
    return 2;
}

var sourcePath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
if (!File.Exists(sourcePath))
    throw new FileNotFoundException("Stock game assembly was not found.", sourcePath);
if (string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The public compile reference must not overwrite the stock game assembly.");

using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(sourcePath)!);
var reader = new ReaderParameters
{
    AssemblyResolver = resolver,
    InMemory = true,
    ReadSymbols = false,
};
using var assembly = AssemblyDefinition.ReadAssembly(sourcePath, reader);
foreach (var type in assembly.MainModule.Types)
    Publicize(type);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
var temporaryPath = outputPath + ".tmp";
try
{
    assembly.Write(temporaryPath, new WriterParameters { WriteSymbols = false });
    File.Move(temporaryPath, outputPath, true);
}
finally
{
    if (File.Exists(temporaryPath))
        File.Delete(temporaryPath);
}

Console.WriteLine($"Generated independent compile reference: {outputPath}");
return 0;

static void Publicize(TypeDefinition type)
{
    if (type.Name != "<Module>")
    {
        type.Attributes &= ~TypeAttributes.VisibilityMask;
        type.Attributes |= type.IsNested ? TypeAttributes.NestedPublic : TypeAttributes.Public;
    }

    var eventNames = type.Events.Select(eventDefinition => eventDefinition.Name)
        .ToHashSet(StringComparer.Ordinal);
    foreach (var field in type.Fields)
    {
        // C# events normally have a same-named private backing field. Exposing
        // both produces an ambiguous symbol; the event's accessors are the API.
        if (eventNames.Contains(field.Name))
            continue;
        field.Attributes &= ~FieldAttributes.FieldAccessMask;
        field.Attributes |= FieldAttributes.Public;
    }

    foreach (var method in type.Methods)
    {
        method.Attributes &= ~MethodAttributes.MemberAccessMask;
        method.Attributes |= MethodAttributes.Public;
    }

    foreach (var nested in type.NestedTypes)
        Publicize(nested);
}
