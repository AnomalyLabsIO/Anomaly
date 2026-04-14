using Anomaly.Launcher.Models;
using Newtonsoft.Json;

namespace Anomaly.Launcher.Services;

public class CrashReportService : IDisposable
{
    private readonly PathService _paths;
    private FileSystemWatcher? _watcher;

    public event EventHandler<CrashReportSummary>? NewCrashReport;

    public CrashReportService(PathService paths)
    {
        _paths = paths;
        StartWatching();
    }

    public List<CrashReportSummary> GetRecentReports(int count = 10)
    {
        var reports = new List<CrashReportSummary>();
        if (!Directory.Exists(_paths.CrashReportsDir))
            return reports;

        var files = Directory.GetFiles(_paths.CrashReportsDir, "crash-*.json")
            .OrderByDescending(File.GetCreationTimeUtc)
            .Take(count);

        foreach (var file in files)
        {
            var report = LoadReport(file);
            if (report != null)
                reports.Add(report);
        }

        return reports;
    }

    public List<CrashReportSummary> GetNewReportsSince(DateTime utcTime)
    {
        var reports = new List<CrashReportSummary>();
        if (!Directory.Exists(_paths.CrashReportsDir))
            return reports;

        var files = Directory.GetFiles(_paths.CrashReportsDir, "crash-*.json")
            .Where(f => File.GetCreationTimeUtc(f) >= utcTime);

        foreach (var file in files)
        {
            var report = LoadReport(file);
            if (report != null)
                reports.Add(report);
        }

        return reports.OrderByDescending(r => r.Timestamp).ToList();
    }

    private void StartWatching()
    {
        if (!Directory.Exists(_paths.CrashReportsDir))
        {
            try
            {
                Directory.CreateDirectory(_paths.CrashReportsDir);
            }
            catch
            {
                return;
            }
        }

        _watcher = new FileSystemWatcher(_paths.CrashReportsDir, "crash-*.json")
        {
            NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnCrashReportCreated;
    }

    private void OnCrashReportCreated(object sender, FileSystemEventArgs e)
    {
        // Small delay to let the file finish writing
        Task.Delay(500).ContinueWith(_ =>
        {
            var report = LoadReport(e.FullPath);
            if (report != null)
                NewCrashReport?.Invoke(this, report);
        });
    }

    private static CrashReportSummary? LoadReport(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<CrashReportSummary>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
