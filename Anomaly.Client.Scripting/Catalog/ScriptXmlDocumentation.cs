using System.Collections.ObjectModel;
using System.Xml.Linq;

namespace Anomaly.Client.Scripting.Catalog;

/// <summary>Tooling-only XML reader; no path or loader is exposed to scripts.</summary>
internal static class ScriptXmlDocumentation
{
    internal static IReadOnlyDictionary<string, string> Read(string path)
    {
        var document = XDocument.Load(path, LoadOptions.None);
        var summaries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in document.Root?.Element("members")?.Elements("member") ?? Enumerable.Empty<XElement>())
        {
            var name = (string?)member.Attribute("name");
            var summary = member.Element("summary");
            if (!string.IsNullOrEmpty(name) && summary != null)
                summaries[name] = string.Join(" ", summary.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
        return new ReadOnlyDictionary<string, string>(summaries);
    }
}
