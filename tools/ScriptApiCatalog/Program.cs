using System.Text;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting;
using Anomaly.Client.Scripting.Catalog;

var luaWorkspace = args.Length is 4 or 5 && args[0] == "--lua-workspace" &&
    (args.Length == 4 || args[4] == "--force");
if (!luaWorkspace && (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--verify")))
{
    Console.Error.WriteLine("Usage: ScriptApiCatalog <output-directory> [--verify]");
    Console.Error.WriteLine("       ScriptApiCatalog --lua-workspace <directory> <game-version> <anomaly-version> [--force]");
    return 2;
}

// Discovery and export inspect trusted metadata only. No VM or script is created.
using var runtime = new ScriptRuntime();
runtime.DiscoverApi();
var catalog = runtime.GetCatalog();
var xmlPath = Path.ChangeExtension(typeof(ScriptTypeAttribute).Assembly.Location, ".xml");
var docs = File.Exists(xmlPath) ? ScriptXmlDocumentation.Read(xmlPath) : null;
if (luaWorkspace)
{
    var result = LuaDefinitionGenerator.Generate(catalog, args[1], args[2], args[3], args.Length == 5, docs);
    Console.WriteLine("Exported Lua editor references to " + result.DefinitionsPath);
    Console.WriteLine(result.ConfigurationPreserved ? "Preserved " + result.ConfigurationPath : "Wrote " + result.ConfigurationPath);
    return 0;
}
var exports = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["ScriptAPI.Shipped.txt"] = catalog.DescribeSurface(),
    ["ScriptAPI.json"] = catalog.ToJson(docs),
    ["Anomaly.Client.Api.d.lua"] = catalog.ToLuaDefinitions(docs),
    ["LuaEnumNames.md"] = catalog.ToEnumNameTable()
};
var directory = Path.GetFullPath(args[0]);
var verify = args.Length == 2;
if (!verify) Directory.CreateDirectory(directory);
var mismatch = false;
foreach (var export in exports)
{
    var path = Path.Combine(directory, export.Key);
    if (verify)
    {
        if (!File.Exists(path) || File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal) != export.Value)
        {
            Console.Error.WriteLine("Script API export differs: " + path);
            mismatch = true;
        }
    }
    else File.WriteAllText(path, export.Value, new UTF8Encoding(false));
}
if (mismatch) return 1;
Console.WriteLine(verify ? "Script API exports match." : "Exported the approved script API to " + directory);
return 0;
