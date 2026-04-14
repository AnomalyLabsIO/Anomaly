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

        var files = Directory.GetFiles(_paths.ModsDir, "*.dll")
            .Concat(Directory.GetFiles(_paths.ModsDir, "*.dll.DISABLED"));

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var isActive = file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                           && !file.EndsWith(".dll.DISABLED", StringComparison.OrdinalIgnoreCase);

            var (assemblyName, version, targetGameVersion) = ReadModMetadata(file);

            mods.Add(new ModInfo
            {
                FileName = fileName,
                FilePath = file,
                IsActive = isActive,
                AssemblyName = assemblyName,
                Version = version,
                TargetGameVersion = targetGameVersion
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

    private static (string? assemblyName, string? version, string? targetGameVersion) ReadModMetadata(string dllPath)
    {
        try
        {
            // For .dll.DISABLED files, we still read the PE metadata
            using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
                return (null, null, null);

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

            return (assemblyName, version, targetGameVersion);
        }
        catch
        {
            return (null, null, null);
        }
    }
}
