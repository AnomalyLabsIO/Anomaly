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
    /// Returns the vanilla game directory path. Delegates to PathService.
    /// </summary>
    public string? GetVanillaDir() => _paths.VanillaDir;

    /// <summary>
    /// Compares GameAssembly.dll last-modified dates between vanilla and modded installs.
    /// Returns null if either file is missing.
    /// </summary>
    public GameAssemblyComparison? CompareGameAssembly()
    {
        var vanillaDir = _paths.VanillaDir;
        if (vanillaDir == null) return null;

        var vanillaGa = Path.Combine(vanillaDir, "GameAssembly.dll");
        var moddedGa = Path.Combine(_paths.LauncherDir, "GameAssembly.dll");

        if (!File.Exists(vanillaGa) || !File.Exists(moddedGa))
            return null;

        var vanillaTime = File.GetLastWriteTimeUtc(vanillaGa);
        var moddedTime = File.GetLastWriteTimeUtc(moddedGa);

        return new GameAssemblyComparison
        {
            VanillaLastModified = vanillaTime,
            ModdedLastModified = moddedTime,
            VanillaIsNewer = vanillaTime > moddedTime
        };
    }

    /// <summary>
    /// Reads the installed game version from version.txt if present.
    /// </summary>
    public string? GetInstalledVersion(string gameDir)
    {
        var versionFile = Path.Combine(gameDir, "version.txt");
        if (File.Exists(versionFile))
        {
            var v = File.ReadAllText(versionFile).Trim();
            if (!string.IsNullOrEmpty(v)) return v;
        }

        return null;
    }

}

public class GameAssemblyComparison
{
    public DateTime VanillaLastModified { get; init; }
    public DateTime ModdedLastModified { get; init; }
    public bool VanillaIsNewer { get; init; }
}
