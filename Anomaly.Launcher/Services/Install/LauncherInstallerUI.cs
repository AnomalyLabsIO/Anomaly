using Anomaly.InstallerCore;
using Avalonia.Threading;

namespace Anomaly.Launcher.Services.Install;

/// <summary>
/// Sink that routes InstallerCore callbacks to the Launcher's progress dialog.
/// All events are marshalled onto the UI thread; the install engine runs on a
/// background thread (see <see cref="InstallerCoordinator"/>) so that thread
/// direction is consistent.
/// </summary>
public sealed class LauncherInstallerUI : IInstallerUI
{
    public event Action<int, int, string>? StepChanged;
    public event Action<string, double, string?>? ProgressUpdated;
    public event Action<InstallerLogLevel, string>? LogEmitted;

    public void Banner()
    {
        // Dialog renders its own header; no-op here.
    }

    public void StepStarted(int stepNumber, int totalSteps, string description)
    {
        Post(() => StepChanged?.Invoke(stepNumber, totalSteps, description));
        Post(() => LogEmitted?.Invoke(InstallerLogLevel.Info, $"[{stepNumber}/{totalSteps}] {description}"));
    }

    public void Log(InstallerLogLevel level, string message)
    {
        Post(() => LogEmitted?.Invoke(level, message));
    }

    public void Progress(string label, long current, long total, string? suffix = null)
    {
        if (total <= 0) return;
        double percent = current * 100.0 / total;
        Post(() => ProgressUpdated?.Invoke(label, percent, suffix));
    }

    public string PromptPath(string prompt)
    {
        // The Launcher always supplies a vanilla path up front, so this prompt path
        // should never execute. Throw a clear error if it ever does.
        throw new InstallerException(
            "Installer asked for a path interactively but the Launcher had no value to provide. " +
            "This is a bug — please report it.");
    }

    public bool PromptYesNo(string question)
    {
        // Same rationale as PromptPath — the Launcher-driven flow never asks for confirmation.
        return false;
    }

    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
