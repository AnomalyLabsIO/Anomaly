using System.Collections.ObjectModel;
using Anomaly.Launcher.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anomaly.Launcher.ViewModels;

public partial class CrashReportDialogViewModel : ObservableObject
{
    public ObservableCollection<CrashReportSummary> Reports { get; }

    [ObservableProperty]
    private CrashReportSummary? _selectedReport;

    public string Title => Reports.Count == 1
        ? "Crash Report"
        : $"Crash Reports ({Reports.Count})";

    public string SelectedReportDetail
    {
        get
        {
            if (SelectedReport == null)
                return string.Empty;

            var r = SelectedReport;
            var lines = new List<string>
            {
                $"Report ID: {r.ReportId}",
                $"Timestamp: {r.Timestamp:yyyy-MM-dd HH:mm:ss UTC}",
                $"Trigger: {r.TriggerType}",
                $"Exception: {r.ExceptionType}",
                $"Message: {r.ExceptionMessage}",
                "",
                "--- Stack Trace ---",
                r.StackTrace ?? "(none)",
            };

            if (r.LoadedMods is { Length: > 0 })
            {
                lines.Add("");
                lines.Add("--- Loaded Mods ---");
                lines.AddRange(r.LoadedMods);
            }

            if (r.SystemInfo != null)
            {
                lines.Add("");
                lines.Add("--- System Info ---");
                lines.Add($"OS: {r.SystemInfo.OperatingSystem}");
                lines.Add($"CPU: {r.SystemInfo.CpuType} ({r.SystemInfo.ProcessorCount} cores)");
                lines.Add($"RAM: {r.SystemInfo.SystemMemoryMb} MB");
                lines.Add($"GPU: {r.SystemInfo.GpuName}");
                lines.Add($"Unity: {r.SystemInfo.UnityVersion}");
                lines.Add($"MelonLoader: {r.SystemInfo.MelonLoaderVersion}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public CrashReportDialogViewModel(IEnumerable<CrashReportSummary> reports)
    {
        Reports = new ObservableCollection<CrashReportSummary>(reports);
        _selectedReport = Reports.FirstOrDefault();
    }

    partial void OnSelectedReportChanged(CrashReportSummary? value)
    {
        OnPropertyChanged(nameof(SelectedReportDetail));
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        // This will be wired up in the view's code-behind using Avalonia clipboard API
    }
}
