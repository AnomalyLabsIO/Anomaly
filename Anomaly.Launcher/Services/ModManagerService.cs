using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Anomaly.Launcher.Models;

namespace Anomaly.Launcher.Services;

public class ModManagerService
{
    private readonly PathService _paths;

    public ModManagerService(PathService paths)
    {
        _paths = paths;
    }

    public List<ModInfo> GetMods()
    {
        var mods = new List<ModInfo>();
        if (!Directory.Exists(_paths.ModsDir))
            return mods;

        var available = BuildAvailableAssemblySet();

        var files = Directory.GetFiles(_paths.ModsDir, "*.dll")
            .Concat(Directory.GetFiles(_paths.ModsDir, "*.dll.DISABLED"));

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var isActive = file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                           && !file.EndsWith(".dll.DISABLED", StringComparison.OrdinalIgnoreCase);

            var meta = ReadModMetadata(file);

            var missing = meta.AssemblyReferences
                .Where(r => !IsRuntimeProvided(r) && !available.Contains(r))
                .ToArray();

            mods.Add(new ModInfo
            {
                FileName = fileName,
                FilePath = file,
                IsActive = isActive,
                AssemblyName = meta.AssemblyName,
                Version = meta.Version,
                TargetGameVersion = meta.TargetGameVersion,
                AssemblyReferences = meta.AssemblyReferences,
                MissingReferences = missing
            });
        }

        return mods.OrderBy(m => m.AssemblyName ?? m.FileName).ToList();
    }

    public void ToggleMod(ModInfo mod)
    {
        string newPath;
        if (mod.IsActive)
        {
            // Deactivate: .dll -> .dll.DISABLED
            newPath = mod.FilePath + ".DISABLED";
        }
        else
        {
            // Activate: .dll.DISABLED -> .dll
            newPath = mod.FilePath.Replace(".dll.DISABLED", ".dll", StringComparison.OrdinalIgnoreCase);
        }

        File.Move(mod.FilePath, newPath);
        mod.FilePath = newPath;
        mod.IsActive = !mod.IsActive;
    }

    // Core BCL assemblies ship with the .NET runtime that MelonLoader hosts
    // (resolved via MelonLoader/net6/MelonLoader.runtimeconfig.json → Microsoft.NETCore.App).
    // They live outside the game dir, so the recursive folder scan won't find them.
    private static bool IsRuntimeProvided(string assemblyName)
    {
        return assemblyName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assemblyName, "System", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assemblyName, "mscorlib", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assemblyName, "netstandard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assemblyName, "WindowsBase", StringComparison.OrdinalIgnoreCase);
    }

    private HashSet<string> BuildAvailableAssemblySet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ScanInto(set, _paths.ModsDir, includeDisabled: true);
        ScanInto(set, _paths.UserLibsDir, includeDisabled: false);
        ScanInto(set, _paths.MelonLoaderDir, includeDisabled: false);
        return set;
    }

    private static void ScanInto(HashSet<string> set, string dir, bool includeDisabled)
    {
        if (!Directory.Exists(dir))
            return;

        var files = Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories);
        if (includeDisabled)
            files = files.Concat(Directory.EnumerateFiles(dir, "*.dll.DISABLED", SearchOption.AllDirectories));

        foreach (var file in files)
        {
            var name = TryReadAssemblyName(file);
            if (name != null)
                set.Add(name);
        }
    }

    private static string? TryReadAssemblyName(string dllPath)
    {
        try
        {
            using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
                return null;

            var metadata = peReader.GetMetadataReader();
            var assemblyDef = metadata.GetAssemblyDefinition();
            return metadata.GetString(assemblyDef.Name);
        }
        catch
        {
            return null;
        }
    }

    private static ModMetadata ReadModMetadata(string dllPath)
    {
        try
        {
            // For .dll.DISABLED files, we still read the PE metadata
            using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
                return new ModMetadata(null, null, null, Array.Empty<string>());

            var metadata = peReader.GetMetadataReader();
            var assemblyDef = metadata.GetAssemblyDefinition();
            var assemblyName = metadata.GetString(assemblyDef.Name);
            var version = assemblyDef.Version.ToString();

            string? targetGameVersion = null;

            // Scan custom attributes for AssemblyMetadataAttribute("TargetGameVersion", "X.Y.Z")
            foreach (var attrHandle in assemblyDef.GetCustomAttributes())
            {
                var attr = metadata.GetCustomAttribute(attrHandle);
                var ctorHandle = attr.Constructor;

                // Check if this is an AssemblyMetadataAttribute
                if (ctorHandle.Kind == HandleKind.MemberReference)
                {
                    var memberRef = metadata.GetMemberReference((MemberReferenceHandle)ctorHandle);
                    var typeRef = metadata.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                    var typeName = metadata.GetString(typeRef.Name);

                    if (typeName == "AssemblyMetadataAttribute")
                    {
                        var blobReader = metadata.GetBlobReader(attr.Value);
                        // Skip prolog (2 bytes)
                        blobReader.ReadUInt16();
                        var key = blobReader.ReadSerializedString();
                        var value = blobReader.ReadSerializedString();

                        if (key == "TargetGameVersion")
                        {
                            targetGameVersion = value;
                            break;
                        }
                    }
                }
            }

            var references = new List<string>();
            foreach (var refHandle in metadata.AssemblyReferences)
            {
                var asmRef = metadata.GetAssemblyReference(refHandle);
                references.Add(metadata.GetString(asmRef.Name));
            }

            return new ModMetadata(assemblyName, version, targetGameVersion, references);
        }
        catch
        {
            return new ModMetadata(null, null, null, Array.Empty<string>());
        }
    }

    private sealed record ModMetadata(
        string? AssemblyName,
        string? Version,
        string? TargetGameVersion,
        IReadOnlyList<string> AssemblyReferences);
}
