using System.Text;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting;
using Anomaly.Client.Scripting.Catalog;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--verify"))
{
    Console.Error.WriteLine("Usage: ScriptApiCatalog <output-directory> [--verify]");
    return 2;
}

// Discovery and export inspect trusted metadata only. No VM or script is created.
using var runtime = new ScriptRuntime();
runtime.DiscoverApi();
var catalog = runtime.GetCatalog();
var xmlPath = Path.ChangeExtension(typeof(ScriptTypeAttribute).Assembly.Location, ".xml");
var docs = File.Exists(xmlPath) ? ScriptXmlDocumentation.Read(xmlPath) : null;
var exports = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["ScriptAPI.Shipped.txt"] = catalog.DescribeSurface(),
    ["ScriptAPI.json"] = catalog.ToJson(docs),
    ["Anomaly.Client.Api.d.lua"] = catalog.ToLuaDefinitions(docs)
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
