namespace Anomaly.Client.API.Features;

public static class Paths
{
    public static string ApplicationDataPath { get; }
    public static string AnomalyPath { get; }

    public static string FileCachePath { get; }
    public static string CrashReportsPath { get; }

    public static string SkinsPath { get; }
    public static string PreloaderSkinPath { get; }

    static Paths()
    {
        ApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AnomalyPath = Path.Combine(ApplicationDataPath, "Anomaly");
        FileCachePath = Path.Combine(AnomalyPath, "FileCache");
        CrashReportsPath = Path.Combine(AnomalyPath, "CrashReports");
        SkinsPath = Path.Combine(AnomalyPath, "Skins");
        PreloaderSkinPath = Path.Combine(SkinsPath, "Preloader");

        if (!Directory.Exists(AnomalyPath))
            Directory.CreateDirectory(AnomalyPath);
        if (!Directory.Exists(SkinsPath))
            Directory.CreateDirectory(SkinsPath);
        if (!Directory.Exists(PreloaderSkinPath))
            Directory.CreateDirectory(PreloaderSkinPath);
    }
}
