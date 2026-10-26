using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Scripting;

namespace Anomaly.Server.Api.Scripting;

/// <summary>Publishes embedded Lua sources through the normal connection content offer.</summary>
public static class ClientScripts
{
    private static readonly object Sync = new object();
    private static readonly Dictionary<string, Registration> Registrations = new Dictionary<string, Registration>(StringComparer.Ordinal);
    private static long _revision;

    /// <summary>
    /// Captures .lua resources below a dotted resource prefix. Dots in their relative names
    /// become directory separators; names are normalized to lowercase. Dispose in the plugin's Disable.
    /// An optional resource contains a {"Messages":[...]} schema declaration.
    /// </summary>
    public static IDisposable RegisterEmbedded(string packId, Assembly assembly, string resourcePrefix, string messageSchemasResource = null)
    {
        if (!AssetPackIndexCodec.IsPackId(packId, true)) throw new ArgumentException("Invalid script pack ID.", nameof(packId));
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        if (string.IsNullOrWhiteSpace(resourcePrefix) || resourcePrefix.Length > 512) throw new ArgumentException("An explicit resource prefix is required.", nameof(resourcePrefix));
        string prefix = resourcePrefix.TrimEnd('.') + ".";
        string[] names = assembly.GetManifestResourceNames();
        if (names.Length > AssetPackSnapshot.MaxScannedEntries) throw new InvalidDataException("Assembly resource traversal budget exceeded.");
        var sources = new List<Source>();
        int total = 0;
        string addressRoot = "mods/" + packId.Replace(':', '/') + "/";
        foreach (string name in names.OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Length > 1024 || sources.Count == ScriptPackLimits.MaxScripts) throw new InvalidDataException("Embedded source count or name budget exceeded.");
            string relative = name.Substring(prefix.Length);
            relative = relative.Substring(0, relative.Length - 4).Replace('.', '/').ToLowerInvariant() + ".lua";
            byte[] bytes;
            using (Stream stream = assembly.GetManifestResourceStream(name)) bytes = ReadBounded(stream, ScriptPackLimits.MaxScriptBytesPerFile);
            ScriptSourceValidator.Validate(bytes);
            total = checked(total + bytes.Length);
            if (total > ScriptPackLimits.MaxScriptBytes) throw new InvalidDataException("Embedded source byte budget exceeded.");
            sources.Add(new Source(addressRoot + relative, bytes));
        }
        var index = new ScriptPackIndex { PackId = packId };
        foreach (Source source in sources)
            index.Sources.Add(new ScriptPackSource { Address = source.Address, SourceBytes = source.Bytes.Length, SourceSha256 = ScriptSourceValidator.HashHex(source.Bytes) });
        if (messageSchemasResource != null)
        {
            if (messageSchemasResource.Length > 1024) throw new ArgumentException("Schema resource name is too long.", nameof(messageSchemasResource));
            using (Stream stream = assembly.GetManifestResourceStream(messageSchemasResource))
                index.Messages = ScriptPackIndexCodec.ParseMessageSchemas(ReadBounded(stream, ScriptPackLimits.MaxIndexBytes));
        }
        ScriptPackIndexCodec.Validate(index);
        var registration = new Registration(index, sources);
        lock (Sync)
        {
            if (Registrations.ContainsKey(packId)) throw new InvalidOperationException("Script pack already registered: " + packId);
            int count = sources.Count, bytes = total, schemas = index.Messages.Count;
            foreach (Registration current in Registrations.Values)
            {
                count = checked(count + current.Sources.Count);
                bytes = checked(bytes + current.Sources.Sum(source => source.Bytes.Length));
                schemas = checked(schemas + current.Index.Messages.Count);
            }
            if (count > ScriptPackLimits.MaxScripts || bytes > ScriptPackLimits.MaxScriptBytes || schemas > ScriptMessageLimits.MaxSchemasPerCatalog)
                throw new InvalidDataException("Registered source or schema inventory exceeded its connection budget.");
            Registrations.Add(packId, registration);
            _revision++;
        }
        ServerAssetManager.InvalidateScriptRegistrations();
        return registration;
    }

    internal static long Revision { get { lock (Sync) return _revision; } }

    internal static Registration[] Capture(out long revision)
    {
        lock (Sync)
        {
            revision = _revision;
            return Registrations.Values.OrderBy(value => value.Index.PackId, StringComparer.Ordinal).ToArray();
        }
    }

    internal static byte[] ReadBounded(Stream stream, int maximum)
    {
        if (stream == null) throw new InvalidDataException("The declared script resource does not exist.");
        using (var result = new MemoryStream())
        {
            var buffer = new byte[Math.Min(4096, maximum + 1)];
            int read;
            while ((read = stream.Read(buffer, 0, Math.Min(buffer.Length, maximum - (int)result.Length + 1))) != 0)
            {
                if (result.Length + read > maximum) throw new InvalidDataException("Script source or schema exceeded its byte budget.");
                result.Write(buffer, 0, read);
            }
            return result.ToArray();
        }
    }

    internal sealed class Source
    {
        internal readonly string Address;
        internal readonly byte[] Bytes;
        internal Source(string address, byte[] bytes) { Address = address; Bytes = bytes; }
    }

    internal sealed class Registration : IDisposable
    {
        internal readonly ScriptPackIndex Index;
        internal readonly List<Source> Sources;
        internal Registration(ScriptPackIndex index, List<Source> sources) { Index = index; Sources = sources; }
        public void Dispose()
        {
            lock (Sync)
            {
                if (!Registrations.TryGetValue(Index.PackId, out Registration active) || !ReferenceEquals(this, active)) return;
                Registrations.Remove(Index.PackId);
                _revision++;
            }
            ServerAssetManager.InvalidateScriptRegistrations();
        }
    }
}
