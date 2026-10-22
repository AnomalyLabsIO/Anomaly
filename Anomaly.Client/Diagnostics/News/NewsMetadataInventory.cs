#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Anomaly.Client.Diagnostics.News;

/// <summary>Lists present managed files without executing assemblies or reproducing loader admission decisions.</summary>
internal static class NewsMetadataInventory
{
    private const int MaximumDirectories = 256;
    private const int MaximumFiles = 1024;
    private const int MaximumDepth = 16;
    private const long MaximumAssemblyBytes = 128L * 1024 * 1024;

    internal static NewsInventoryResult Scan(string modsDirectory, string pluginsDirectory, string userLibsDirectory,
        bool disableSubfolderLoad, bool disableSubfolderManifest, IReadOnlyList<DiagnosticComponent> known,
        Func<string, string> redact = null)
    {
        var components = new List<DiagnosticComponent>();
        var issues = new List<DiagnosticIssue>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownPaths = new Dictionary<string, List<DiagnosticComponent>>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in known ?? Array.Empty<DiagnosticComponent>())
        {
            if (component == null) continue;
            string normalized = NewsDiagnosticIdentity.NormalizePath(component.Path);
            if (normalized.Length == 0) continue;
            if (!knownPaths.TryGetValue(normalized, out List<DiagnosticComponent> entries))
                knownPaths.Add(normalized, entries = new List<DiagnosticComponent>());
            entries.Add(component);
        }
        int directories = 0;
        int files = 0;
        bool limited = false;

        void Visit(string path, DiagnosticComponentKind kind, int depth, bool requiresManifest)
        {
            if (limited || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            if (depth > MaximumDepth || ++directories > MaximumDirectories) { limited = true; return; }
            try
            {
                // Reparse points can leave the loader's installation or form cycles. Registries still
                // retain actual loaded entries from such paths; discovery does not follow them.
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
                foreach (string file in Directory.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    if (++files > MaximumFiles) { limited = true; return; }
                    string normalized = NewsDiagnosticIdentity.NormalizePath(file);
                    if (normalized.Length == 0 || !seenPaths.Add(normalized)) continue;
                    knownPaths.TryGetValue(normalized, out List<DiagnosticComponent> existing);
                    // Fully described live entries are authoritative and need no filesystem read.
                    if (existing != null && existing.All(component => !string.IsNullOrEmpty(component.Name) && !string.IsNullOrEmpty(component.Version)))
                        continue;
                    ReadFile(file, kind, existing?.ToArray(), components, issues, redact);
                }
                if (disableSubfolderLoad) return;
                foreach (string child in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(child);
                    if (name.StartsWith("~", StringComparison.Ordinal) || name.StartsWith(".", StringComparison.Ordinal) ||
                        name == "Broken" || name == "Retired" || name == "Disabled") continue;
                    // In 0.7.3 this is an existence marker, not a parsed document. Only entry
                    // into an immediate root subfolder needs one; descendants are ungated.
                    if (requiresManifest && !disableSubfolderManifest && !File.Exists(Path.Combine(child, "manifest.json")))
                        continue;
                    var childKind = name == "UserLibs" ? DiagnosticComponentKind.Dependency :
                        name == "Plugins" && kind == DiagnosticComponentKind.Mod ? DiagnosticComponentKind.Plugin : kind;
                    Visit(child, childKind, depth + 1, requiresManifest: false);
                    if (limited) return;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is System.Security.SecurityException)
            {
                issues.Add(NewsDiagnosticIdentity.Unavailable("inventory", "inventory-directory-unavailable",
                    "An installed-component directory could not be inspected (" + exception.GetType().Name + ").", false));
            }
        }

        Visit(modsDirectory, DiagnosticComponentKind.Mod, 0, requiresManifest: true);
        Visit(pluginsDirectory, DiagnosticComponentKind.Plugin, 0, requiresManifest: true);
        Visit(userLibsDirectory, DiagnosticComponentKind.Dependency, 0, requiresManifest: true);
        if (limited)
            issues.Add(NewsDiagnosticIdentity.Unavailable("inventory", "inventory-limit",
                "Additional installed files were omitted after the discovery limit.", false));
        return new NewsInventoryResult { Components = components.ToArray(), Issues = issues.ToArray() };
    }

    private static void ReadFile(string path, DiagnosticComponentKind kind, DiagnosticComponent[] existing, List<DiagnosticComponent> components,
        List<DiagnosticIssue> issues, Func<string, string> redact)
    {
        string assemblyName = Path.GetFileNameWithoutExtension(path);
        string version = string.Empty;
        string name = string.Empty;
        string id = string.Empty;
        string typeName = string.Empty;
        bool hasMelonInfo = false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumAssemblyBytes)
                throw new InvalidDataException("Assembly exceeds diagnostic metadata budget.");
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata) return; // Native DLLs have no managed registration contract.
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) return;
            var assembly = metadata.GetAssemblyDefinition();
            assemblyName = metadata.GetString(assembly.Name);
            version = assembly.Version.ToString();
            foreach (var handle in assembly.GetCustomAttributes())
            {
                var attribute = metadata.GetCustomAttribute(handle);
                string attributeName = AttributeName(metadata, attribute.Constructor);
                if (attributeName != "MelonLoader.MelonInfoAttribute" && attributeName != "MelonLoader.MelonIDAttribute")
                    continue;
                var blob = metadata.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1) throw new BadImageFormatException("Invalid custom attribute prologue.");
                if (attributeName == "MelonLoader.MelonInfoAttribute")
                {
                    hasMelonInfo = true;
                    string serializedType = blob.ReadSerializedString() ?? string.Empty;
                    int comma = serializedType.IndexOf(',');
                    typeName = comma < 0 ? serializedType : serializedType.Substring(0, comma).Trim();
                    name = blob.ReadSerializedString() ?? string.Empty;
                    int parameters = ParameterCount(metadata, attribute.Constructor);
                    if (parameters == 5)
                        version = blob.ReadSerializedString() ?? version;
                    else if (parameters == 7 || parameters == 8)
                    {
                        int major = blob.ReadInt32();
                        int minor = blob.ReadInt32();
                        int revision = blob.ReadInt32();
                        string identifier = parameters == 8 ? blob.ReadSerializedString() ?? string.Empty : string.Empty;
                        version = major + "." + minor + "." + revision + identifier;
                    }
                    else throw new BadImageFormatException("Unsupported MelonInfo constructor.");
                }
                else id = blob.ReadSerializedString() ?? string.Empty;
            }
            if (kind != DiagnosticComponentKind.Dependency && !hasMelonInfo) return;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                          exception is BadImageFormatException || exception is InvalidOperationException ||
                                          exception is ArgumentException || exception is System.Security.SecurityException)
        {
            // Reading fails independently of loader execution. Preserve the file as present,
            // and explain our missing evidence without manufacturing a load failure.
            issues.Add(NewsDiagnosticIdentity.Unavailable("inventory", "inventory-metadata-unavailable",
                NewsDiagnosticIdentity.SafeText("Assembly metadata could not be inspected for " + Path.GetFileName(path) +
                    " (" + exception.GetType().Name + ").", redact), false));
        }
        if (existing != null)
        {
            var matches = existing.Where(component => string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(component.TypeName) ||
                string.Equals(typeName, component.TypeName, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) return;
            var original = matches[0];
            components.Add(new DiagnosticComponent
            {
                Key = original.Key,
                Name = string.IsNullOrEmpty(original.Version) && name.Length != 0 || string.IsNullOrEmpty(original.Name)
                    ? name.Length == 0 ? assemblyName : name : original.Name,
                Version = string.IsNullOrEmpty(original.Version) ? version : original.Version,
                Id = string.IsNullOrEmpty(original.Id) ? id : original.Id,
                Path = original.Path,
                TypeName = string.IsNullOrEmpty(original.TypeName) ? typeName : original.TypeName,
                Kind = original.Kind,
                State = original.State,
            });
            return;
        }
        components.Add(new DiagnosticComponent
        {
            Key = NewsDiagnosticIdentity.ComponentKey(kind, path, id, typeName),
            Name = name.Length == 0 ? assemblyName : name,
            Version = version,
            Id = id,
            Path = NewsDiagnosticIdentity.NormalizePath(path),
            TypeName = typeName,
            Kind = kind,
            State = DiagnosticLoadState.Present,
        });
    }

    private static int ParameterCount(MetadataReader metadata, EntityHandle constructor)
    {
        BlobHandle signature = constructor.Kind switch
        {
            HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)constructor).Signature,
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).Signature,
            _ => throw new BadImageFormatException("Unsupported attribute constructor."),
        };
        var reader = metadata.GetBlobReader(signature);
        var header = reader.ReadSignatureHeader();
        if (header.IsGeneric) reader.ReadCompressedInteger();
        return reader.ReadCompressedInteger();
    }

    private static string AttributeName(MetadataReader metadata, EntityHandle constructor)
    {
        EntityHandle type = constructor.Kind switch
        {
            HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default,
        };
        if (type.Kind == HandleKind.TypeReference)
        {
            var reference = metadata.GetTypeReference((TypeReferenceHandle)type);
            return metadata.GetString(reference.Namespace) + "." + metadata.GetString(reference.Name);
        }
        if (type.Kind == HandleKind.TypeDefinition)
        {
            var definition = metadata.GetTypeDefinition((TypeDefinitionHandle)type);
            return metadata.GetString(definition.Namespace) + "." + metadata.GetString(definition.Name);
        }
        return string.Empty;
    }
}
