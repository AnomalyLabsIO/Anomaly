using Anomaly.InstallerCore;

namespace Anomaly.Launcher.Services.Install;

/// <summary>
/// Drives a single in-process install run: swaps the ConsoleUI sink to a
/// <see cref="LauncherInstallerUI"/>, executes <see cref="InstallerService.Install"/>
/// on a background thread, and restores the previous sink on completion.
/// </summary>
public sealed class InstallerCoordinator
{
    private readonly CancellationTokenSource _cts = new();

    public LauncherInstallerUI Ui { get; } = new();
    public CancellationToken Token => _cts.Token;

    public void Cancel() => _cts.Cancel();

    public Task RunInstallAsync(InstallOptions options)
    {
        return Task.Run(() =>
        {
            IInstallerUI previousSink = ConsoleUI.SetSink(Ui);
            try
            {
                InstallerService.Install(options, _cts.Token);
            }
            finally
            {
                ConsoleUI.SetSink(previousSink);
            }
        }, _cts.Token);
    }

    public Task<int> RunUninstallAsync(string installerDir)
    {
        return Task.Run(() =>
        {
            IInstallerUI previousSink = ConsoleUI.SetSink(Ui);
            try
            {
                return InstallerService.Uninstall(installerDir);
            }
            finally
            {
                ConsoleUI.SetSink(previousSink);
            }
        }, _cts.Token);
    }

    public Task<int> RunRemoveGameFilesAsync(string installerDir)
    {
        return Task.Run(() =>
        {
            IInstallerUI previousSink = ConsoleUI.SetSink(Ui);
            try
            {
                return InstallerService.RemoveGameFiles(installerDir);
            }
            finally
            {
                ConsoleUI.SetSink(previousSink);
            }
        }, _cts.Token);
    }
}
