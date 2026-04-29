using Newtonsoft.Json;

namespace Anomaly.InstallerCore;

/// <summary>
/// Persistent record of the currently-installed game in a launcher directory. Written by
/// <see cref="InstallerService.Install"/> on success and read by the launcher to populate
/// the "Currently Installed" panel and to drive Reinstall/Uninstall flows.
/// </summary>
public sealed record InstallState(
    string SchemaVersion,
    string Source,
    string? ManifestId,
    string? BuildId,
    string? GameVersion,
    string InstalledAt);

public static class InstallStateSidecar
{
    public const string FileName = ".anomaly_install.json";
    public const string CurrentSchemaVersion = "1";

    public static string PathFor(string installerDir) => Path.Combine(installerDir, FileName);

    public static void Write(string installerDir, InstallState state)
    {
        Directory.CreateDirectory(installerDir);
        var json = JsonConvert.SerializeObject(state, Formatting.Indented);
        File.WriteAllText(PathFor(installerDir), json);
    }

    public static InstallState? TryRead(string installerDir)
    {
        var path = PathFor(installerDir);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<InstallState>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Delete(string installerDir)
    {
        var path = PathFor(installerDir);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort.
        }
    }
}
