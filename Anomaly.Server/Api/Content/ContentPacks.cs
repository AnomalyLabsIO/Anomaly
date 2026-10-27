using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Content.Packs;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using InventorySystem;

namespace Anomaly.Server.Api.Content;

/// <summary>Publishes explicitly declared embedded content packs through authenticated offers.</summary>
public static class ContentPacks
{
    private const int MaxBundleBytes = 64 * 1024 * 1024;
    private const long MaxEmbeddedBundleBytes = 256L * 1024 * 1024;
    private static readonly Dictionary<string, Registration> Registrations = new Dictionary<string, Registration>(StringComparer.Ordinal);
    private static long _revision;

    /// <summary>Captures resourceRoot/pack.json and its exact slash-separated resource names. Definitions are restart-owned once frozen.</summary>
    public static IDisposable RegisterEmbedded(Assembly assembly, string resourceRoot)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        if (string.IsNullOrWhiteSpace(resourceRoot) || resourceRoot.Length > 512)
            throw new ArgumentException("An explicit resource root is required.", nameof(resourceRoot));
        string root = resourceRoot.TrimEnd('/');
        lock (ServerAssetManager.Sync)
        {
            byte[] Read(string path, int maximum)
            {
                using (Stream stream = assembly.GetManifestResourceStream(root + "/" + path))
                    return ContentPackFiles.ReadBounded(stream, maximum);
            }
            var descriptor = ContentPackCodec.Parse(Read("pack.json", ContentPackCodec.MaxDescriptorBytes));
            if (Registrations.ContainsKey(descriptor.PackId)) throw new InvalidOperationException("Content pack already registered: " + descriptor.PackId);
            int remainingSources = ScriptPackLimits.MaxScripts - Registrations.Values.Count(value => value.Pack.Contents.ScriptIndex != null);
            int remainingBytes = ScriptPackLimits.MaxScriptBytes - Registrations.Values.Sum(value => value.Pack.Contents.SourceBytes?.Length ?? 0);
            if (descriptor.Scripts != null && remainingSources == 0) throw new InvalidDataException("Too many embedded script sources.");
            var contents = ContentPackCodec.Capture(descriptor, (path, maximum) => Read(path,
                descriptor.Scripts != null && path == descriptor.Scripts.EntryPoint ? Math.Min(maximum, remainingBytes) : maximum));
            if (contents.Items.Length != 0 && ContentRegistry.Frozen)
                throw new InvalidOperationException("Item definitions are frozen. Restart the server to change packs.");
            byte[] bundle = null;
            if (contents.AssetIndex != null)
            {
                if (contents.AssetIndex.BundleBytes > MaxBundleBytes) throw new InvalidDataException("Embedded bundle exceeds 64 MiB.");
                if (Registrations.Values.Sum(value => value.Pack.Bundle?.LongLength ?? 0) + contents.AssetIndex.BundleBytes > MaxEmbeddedBundleBytes)
                    throw new InvalidDataException("Embedded bundles exceed 256 MiB.");
                bundle = Read(contents.BundlePath, checked((int)contents.AssetIndex.BundleBytes));
                if (bundle.LongLength != contents.AssetIndex.BundleBytes || ScriptSourceValidator.HashHex(bundle) != contents.AssetIndex.BundleSha256)
                    throw new InvalidDataException("Embedded bundle does not match its declared size and SHA-256.");
            }
            var registration = new Registration(new ContentPackFiles.Pack(contents, null, bundle));
            ValidateInventory(Registrations.Values.Select(value => value.Pack).Concat(new[] { registration.Pack }));
            Registrations.Add(descriptor.PackId, registration);
            _revision++;
            ServerAssetManager.InvalidateContentRegistrations();
            return registration;
        }
    }

    internal static void ValidateInventory(IEnumerable<ContentPackFiles.Pack> packs)
    {
        var logical = new HashSet<string>(StringComparer.Ordinal);
        var components = new Dictionary<string, string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int sources = 0, sourceBytes = 0, schemas = 0, items = 0;
        long aggregate = 0;
        foreach (var pack in packs)
        {
            var c = pack.Contents;
            string id = c.Descriptor.PackId;
            if (!logical.Add(id) || logical.Count > ContentPackCodec.MaxPacks) throw new InvalidDataException("Duplicate or excessive logical content packs.");
            foreach (string component in new[] { c.ScriptIndex?.PackId, c.AssetIndex?.PackId }.Where(value => value != null))
            {
                if (components.ContainsKey(component)) throw new InvalidDataException("Duplicate component pack ID: " + component);
                components.Add(component, id);
            }
            if (c.ScriptIndex != null)
            {
                sources++;
                sourceBytes = checked(sourceBytes + c.SourceBytes.Length);
                schemas = checked(schemas + c.ScriptIndex.Messages.Count);
                aggregate += c.SourceBytes.Length + c.ScriptIndexBytes.Length;
            }
            if (c.AssetIndex != null) aggregate += c.AssetIndexBytes.Length + c.AssetIndex.BundleBytes;
            items = checked(items + c.Items.Length);
            foreach (var item in c.Items)
            {
                if (!keys.Add(item.Key)) throw new InvalidDataException("Duplicate descriptor item key: " + item.Key);
                if (!Enum.TryParse(item.BaseTemplate, false, out ItemType template) || !Enum.IsDefined(typeof(ItemType), item.BaseTemplate) ||
                    (int)template < 0 || (int)template > 127)
                    throw new InvalidDataException("Unsupported vanilla item template: " + item.BaseTemplate);
            }
            if (sources > ScriptPackLimits.MaxScripts || sourceBytes > ScriptPackLimits.MaxScriptBytes || schemas > ScriptMessageLimits.MaxSchemasPerCatalog ||
                items > 127 || aggregate > FileManifestMessage.MaxAggregateBytes)
                throw new InvalidDataException("Content pack inventory exceeds its connection budget.");
        }
        foreach (var pair in components)
            if (logical.Contains(pair.Key) && pair.Key != pair.Value) throw new InvalidDataException("Component ID collides with another logical pack: " + pair.Key);
    }

    internal static long Revision { get { lock (ServerAssetManager.Sync) return _revision; } }
    internal static ContentPackFiles.Pack[] Capture(out long revision)
    {
        lock (ServerAssetManager.Sync)
        {
            revision = _revision;
            return Registrations.Values.OrderBy(value => value.Pack.Contents.Descriptor.PackId, StringComparer.Ordinal).Select(value => value.Pack).ToArray();
        }
    }

    private sealed class Registration : IDisposable
    {
        internal ContentPackFiles.Pack Pack { get; private set; }
        internal Registration(ContentPackFiles.Pack pack) => Pack = pack;
        public void Dispose()
        {
            lock (ServerAssetManager.Sync)
            {
                if (Pack == null) return;
                string id = Pack.Contents.Descriptor.PackId;
                if (!Registrations.TryGetValue(id, out var current) || !ReferenceEquals(current, this)) return;
                // Native definitions remain frozen. Removing a defining pack invalidates
                // new admissions until restart; existing retained offers are unaffected.
                Registrations.Remove(id);
                Pack = null; // Captured scans retain their own independent reference.
                _revision++;
                ServerAssetManager.InvalidateContentRegistrations();
            }
        }
    }
}
