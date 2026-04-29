using System.Collections.ObjectModel;
using Anomaly.InstallerCore;
using Anomaly.Launcher.Services.Install;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anomaly.Launcher.ViewModels;

public enum InstallPhase
{
    Running,
    Success,
    Failed,
    Cancelled
}

public partial class InstallProgressDialogViewModel : ObservableObject
{
    private readonly InstallerCoordinator _coordinator;

    public event Action? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsCancelled))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    private InstallPhase _phase = InstallPhase.Running;

    [ObservableProperty]
    private int _currentStep;

    [ObservableProperty]
    private int _totalSteps = InstallerService.TotalSteps;

    [ObservableProperty]
    private string _stepDescription = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressLabel = string.Empty;

    [ObservableProperty]
    private string _progressSuffix = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool IsRunning => Phase == InstallPhase.Running;
    public bool IsSuccess => Phase == InstallPhase.Success;
    public bool IsFailed => Phase == InstallPhase.Failed;
    public bool IsCancelled => Phase == InstallPhase.Cancelled;
    public bool IsDone => Phase != InstallPhase.Running;

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public InstallProgressDialogViewModel(InstallerCoordinator coordinator)
    {
        _coordinator = coordinator;
        _coordinator.Ui.StepChanged += OnStepChanged;
        _coordinator.Ui.ProgressUpdated += OnProgressUpdated;
        _coordinator.Ui.LogEmitted += OnLogEmitted;
    }

    /// <summary>Drives a running install to completion and updates Phase accordingly.</summary>
    public async Task TrackAsync(Task installTask)
    {
        try
        {
            await installTask.ConfigureAwait(true);
            Phase = InstallPhase.Success;
        }
        catch (OperationCanceledException)
        {
            Phase = InstallPhase.Cancelled;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Phase = InstallPhase.Failed;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsRunning)
        {
            _coordinator.Cancel();
            return;
        }

        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    private void OnStepChanged(int step, int total, string description)
    {
        CurrentStep = step;
        TotalSteps = total;
        StepDescription = description;
        ProgressPercent = 0;
        ProgressLabel = string.Empty;
        ProgressSuffix = string.Empty;
    }

    private void OnProgressUpdated(string label, double percent, string? suffix)
    {
        ProgressLabel = label;
        ProgressPercent = percent;
        ProgressSuffix = suffix ?? string.Empty;
    }

    private void OnLogEmitted(InstallerLogLevel level, string message)
    {
        LogEntries.Add(new LogEntry(level, message));

        // Keep the log bounded so the dialog doesn't balloon during long installs.
        const int maxEntries = 500;
        while (LogEntries.Count > maxEntries)
            LogEntries.RemoveAt(0);
    }
}

public sealed record LogEntry(InstallerLogLevel Level, string Message);
