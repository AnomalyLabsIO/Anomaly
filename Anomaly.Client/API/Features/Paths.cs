namespace Anomaly.Client.API.Features;

public static class Paths
{
    public static string ApplicationDataPath { get; }
    public static string AnomalyPath { get; }

    static Paths()
    {
        ApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AnomalyPath = Path.Combine(ApplicationDataPath, "Anomaly");
        if(!Directory.Exists(AnomalyPath))
            Directory.CreateDirectory(AnomalyPath);
    }
}
