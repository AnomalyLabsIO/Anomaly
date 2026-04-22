namespace Anomaly.InstallerCore;

public enum InstallerLogLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Host abstraction for installer output and prompts. The CLI wraps Console;
/// the Launcher wires this to an Avalonia progress dialog.
/// </summary>
public interface IInstallerUI
{
    void Banner();
    void StepStarted(int stepNumber, int totalSteps, string description);
    void Log(InstallerLogLevel level, string message);
    void Progress(string label, long current, long total, string? suffix = null);
    string PromptPath(string prompt);
    bool PromptYesNo(string question);
}
