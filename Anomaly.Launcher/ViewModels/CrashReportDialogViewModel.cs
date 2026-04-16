using System.Collections.ObjectModel;
using Anomaly.Launcher.Models;
using Anomaly.Launcher.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anomaly.Launcher.ViewModels;

public partial class CrashReportDialogViewModel : ObservableObject
{
    public ObservableCollection<CrashReportSummary> Reports { get; }

    [ObservableProperty]
    private CrashReportSummary? _selectedReport;

    public string Title => Reports.Count == 1
        ? Tr.Get(TrKeys.Crash_Title_Single)
        : Tr.Get(TrKeys.Crash_Title_Multiple, Reports.Count);

    public string SelectedReportDetail
    {
        get
        {
            if (SelectedReport == null)
                return string.Empty;

            var r = SelectedReport;
            var lines = new List<string>
            {
                Tr.Get(TrKeys.Crash_Label_ReportId, r.ReportId ?? string.Empty),
                Tr.Get(TrKeys.Crash_Label_Timestamp, r.Timestamp),
                Tr.Get(TrKeys.Crash_Label_Trigger, r.TriggerType ?? string.Empty),
                Tr.Get(TrKeys.Crash_Label_Exception, r.ExceptionType ?? string.Empty),
                Tr.Get(TrKeys.Crash_Label_Message, r.ExceptionMessage ?? string.Empty),
                "",
                Tr.Get(TrKeys.Crash_Header_StackTrace),
                r.StackTrace ?? Tr.Get(TrKeys.Crash_None),
            };

            if (r.LoadedMods is { Length: > 0 })
            {
                lines.Add("");
                lines.Add(Tr.Get(TrKeys.Crash_Header_LoadedMods));
                lines.AddRange(r.LoadedMods);
            }

            if (r.SystemInfo != null)
            {
                lines.Add("");
                lines.Add(Tr.Get(TrKeys.Crash_Header_SystemInfo));
                lines.Add(Tr.Get(TrKeys.Crash_Label_OS, r.SystemInfo.OperatingSystem ?? string.Empty));
                lines.Add(Tr.Get(TrKeys.Crash_Label_CPU, r.SystemInfo.CpuType ?? string.Empty, r.SystemInfo.ProcessorCount));
                lines.Add(Tr.Get(TrKeys.Crash_Label_RAM, r.SystemInfo.SystemMemoryMb));
                lines.Add(Tr.Get(TrKeys.Crash_Label_GPU, r.SystemInfo.GpuName ?? string.Empty));
                lines.Add(Tr.Get(TrKeys.Crash_Label_Unity, r.SystemInfo.UnityVersion ?? string.Empty));
                lines.Add(Tr.Get(TrKeys.Crash_Label_MelonLoader, r.SystemInfo.MelonLoaderVersion ?? string.Empty));
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
