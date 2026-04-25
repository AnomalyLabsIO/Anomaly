using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Anomaly.InstallerCore.Platform;
using Anomaly.Launcher.Services;
using Anomaly.Launcher.Services.Install;
using Anomaly.Launcher.Services.Localization;
using Anomaly.Launcher.Services.Platform;
using Anomaly.Launcher.Services.Steam;
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

            // Initialize localization before any window is constructed so AXAML TrExtension lookups resolve.
            Tr.Register(new LocalizationService(paths.AnomalyDataDir));

            var userProfile = new UserProfileService(paths);
            var modManager = new ModManagerService(paths);
            var platform = new PlatformInfo();
            var gameProcess = new GameProcessService(paths, platform);
            var crashReport = new CrashReportService(paths);
            var versionCheck = new VersionCheckService(paths);
            var systemInfo = new SystemInfoService(paths);
            var steamDownload = new SteamDownloadService(paths);
            var buildCatalog = new BuildCatalogService();

            var platformOptions = PlatformOptions.Load(paths);
            var platformClient = new PlatformApiClient(platformOptions);
            var installedMods = new InstalledModsService(paths);
            var modInstaller = new ModInstaller(paths, platformClient, installedMods);

            // Make sure anomaly-launcher:// links in browsers route to this exe.
            UrlProtocolService.RegisterOnStartup(Environment.ProcessPath ?? AppContext.BaseDirectory);

            var vm = new MainWindowViewModel(
                paths, userProfile, modManager, gameProcess, crashReport, versionCheck, systemInfo,
                steamDownload, buildCatalog,
                platformClient, modInstaller, installedMods);

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            // If the launcher was started by a deep link, kick off the matching install.
            var deepLink = UrlProtocolService.TryParseFromArgs(desktop.Args ?? Array.Empty<string>());
            if (deepLink != null)
                vm.HandleDeepLinkInstall(deepLink);

            desktop.ShutdownRequested += (_, _) =>
            {
                gameProcess.KillGame();
                vm.Dispose();
                steamDownload.Dispose();
                platformClient.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
