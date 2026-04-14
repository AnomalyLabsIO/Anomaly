namespace Anomaly.Client.API.Features;

public static class Paths
{
    public static string ApplicationDataPath { get; }
    public static string AnomalyPath { get; }

    public static string FileCachePath { get; }
    public static string CrashReportsPath { get; }

    static Paths()
    {
        ApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AnomalyPath = Path.Combine(ApplicationDataPath, "Anomaly");
        FileCachePath = Path.Combine(AnomalyPath, "FileCache");
        CrashReportsPath = Path.Combine(AnomalyPath, "CrashReports");
        if(!Directory.Exists(AnomalyPath))
            Directory.CreateDirectory(AnomalyPath);
    }
}
