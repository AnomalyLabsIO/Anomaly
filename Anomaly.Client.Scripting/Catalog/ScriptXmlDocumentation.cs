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
                summaries[name] = string.Join(" ", ReadText(summary).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
        return new ReadOnlyDictionary<string, string>(summaries);
    }

    private static string ReadText(XContainer container) => string.Concat(container.Nodes().Select(node => node switch
    {
        XText text => text.Value,
        XElement element => ReadElement(element),
        _ => string.Empty
    }));

    private static string ReadElement(XElement element)
    {
        switch (element.Name.LocalName)
        {
            case "see":
            case "seealso":
                var label = ReadText(element);
                if (!string.IsNullOrWhiteSpace(label)) return label;
                var reference = (string?)element.Attribute("cref");
                if (reference?.Length > 2 && reference[1] == ':') reference = reference[2..];
                return (string?)element.Attribute("langword") ?? reference ?? (string?)element.Attribute("href") ?? string.Empty;
            case "paramref":
            case "typeparamref":
                return (string?)element.Attribute("name") ?? ReadText(element);
            case "para":
            case "code":
            case "list":
            case "item":
            case "term":
            case "description":
            case "br":
                return " " + ReadText(element) + " ";
            default:
                return ReadText(element);
        }
    }
}
