using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Anomaly.Launcher.Services;

public class SystemInfoService
{
    private readonly PathService _paths;

    public SystemInfoService(PathService paths)
    {
        _paths = paths;
    }

    public string? GetMelonLoaderVersion()
    {
        // MelonLoader.dll lives in {LauncherDir}/MelonLoader/net6/MelonLoader.dll
        var mlDll = Path.Combine(_paths.LauncherDir, "MelonLoader", "net6", "MelonLoader.dll");
        if (!File.Exists(mlDll))
            return null;

        try
        {
            using var stream = new FileStream(mlDll, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return null;
            var metadata = peReader.GetMetadataReader();
            var assemblyDef = metadata.GetAssemblyDefinition();
            return assemblyDef.Version.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the modded GameAssembly.dll's last-modified timestamp, or null if not present.
    /// Used by the Installed tab to surface "when did this install last get patched?".
    /// </summary>
    public DateTime? GetModdedGameAssemblyTimestamp()
    {
        var moddedGa = Path.Combine(_paths.LauncherDir, "GameAssembly.dll");
        if (!File.Exists(moddedGa))
            return null;
        try
        {
            return File.GetLastWriteTimeUtc(moddedGa);
        }
        catch
        {
            return null;
        }
    }
}
