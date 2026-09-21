using System.Text;

if (args.Length == 1 && args[0] == "--self-test")
    return CompatibilityFixtures.Run();

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: PublicApiBaseline <assembly.dll> <baseline.txt> [--update] | --self-test");
    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
var baselinePath = Path.GetFullPath(args[1]);
var update = args.Length == 3 && string.Equals(args[2], "--update", StringComparison.Ordinal);
if (args.Length == 3 && !update)
{
    Console.Error.WriteLine("The only supported third argument is --update.");
    return 2;
}
if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine("Assembly not found: " + assemblyPath);
    return 2;
}

var current = PublicApiSurface.Generate(assemblyPath);
if (update)
{
    Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
    File.WriteAllText(baselinePath, current, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine("Updated public API baseline: " + baselinePath);
    return 0;
}
if (!File.Exists(baselinePath))
{
    Console.Error.WriteLine("Public API baseline not found: " + baselinePath);
    return 1;
}

var expected = Normalize(File.ReadAllText(baselinePath));
if (string.Equals(expected, current, StringComparison.Ordinal))
{
    Console.WriteLine("Public API baseline matches.");
    return 0;
}

var expectedLines = expected.Split('\n').ToHashSet(StringComparer.Ordinal);
var currentLines = current.Split('\n').ToHashSet(StringComparer.Ordinal);
Console.Error.WriteLine("Public API baseline changed. Review compatibility, then update intentionally.");
foreach (var removed in expectedLines.Except(currentLines).OrderBy(line => line, StringComparer.Ordinal).Take(50))
    Console.Error.WriteLine("- " + removed);
foreach (var added in currentLines.Except(expectedLines).OrderBy(line => line, StringComparer.Ordinal).Take(50))
    Console.Error.WriteLine("+ " + added);
return 1;

static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal)
    .Replace('\r', '\n');
