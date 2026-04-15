using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Anomaly.Launcher.Services;
using Anomaly.Launcher.ViewModels;
using Anomaly.Launcher.Views;

namespace Anomaly.Launcher;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = new PathService();
            var userProfile = new UserProfileService(paths);
            var modManager = new ModManagerService(paths);
            var gameProcess = new GameProcessService(paths);
            var crashReport = new CrashReportService(paths);
            var versionCheck = new VersionCheckService(paths);
            var systemInfo = new SystemInfoService(paths);

            var vm = new MainWindowViewModel(
                paths, userProfile, modManager, gameProcess, crashReport, versionCheck, systemInfo);

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                gameProcess.KillGame();
                vm.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
