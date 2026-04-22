using MelonLoader.Utils;

namespace Anomaly.Client.Api.Features;

public static class Paths
{
    public static string ApplicationDataPath { get; }
    public static string AnomalyPath { get; }

    public static string FileCachePath { get; }
    public static string CrashReportsPath { get; }

    public static string AnomalyUserDataPath { get; }
    public static string SkinsPath { get; }

    static Paths()
    {
        ApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AnomalyPath = Path.Combine(ApplicationDataPath, "Anomaly");
        FileCachePath = Path.Combine(AnomalyPath, "FileCache");
        CrashReportsPath = Path.Combine(AnomalyPath, "CrashReports");
        if (!Directory.Exists(AnomalyPath))
            Directory.CreateDirectory(AnomalyPath);

        AnomalyUserDataPath = Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly");
        SkinsPath = Path.Combine(AnomalyUserDataPath, "Skins");
        if (!Directory.Exists(AnomalyUserDataPath))
            Directory.CreateDirectory(AnomalyUserDataPath);
        if (!Directory.Exists(SkinsPath))
            Directory.CreateDirectory(SkinsPath);
    }
}
