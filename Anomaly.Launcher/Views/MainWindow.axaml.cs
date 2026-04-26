using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Anomaly.Launcher.ViewModels;

namespace Anomaly.Launcher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            // Wire clipboard access — Avalonia requires a Window reference
            vm._copyToClipboard = async text =>
            {
                if (Clipboard != null)
                    await Clipboard.SetTextAsync(text);
            };

            // Wire folder picker for "Use local directory…" install flow.
            vm._pickLocalGameDirectory = PickLocalGameDirectoryAsync;

            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.ShowCrashDialog)
                    && vm.ShowCrashDialog && vm.CrashDialogViewModel != null)
                {
                    ShowCrashReportDialog(vm);
                }

                if (args.PropertyName == nameof(MainWindowViewModel.ShowSteamDownloadDialog)
                    && vm.ShowSteamDownloadDialog && vm.SteamDownloadDialogViewModel != null)
                {
                    ShowSteamDownloadDialog(vm);
                }

                if (args.PropertyName == nameof(MainWindowViewModel.ShowInstallProgressDialog)
                    && vm.ShowInstallProgressDialog && vm.InstallProgressDialogViewModel != null)
                {
                    ShowInstallProgressDialog(vm);
                }

                // Focus the username textbox when entering edit mode
                if (args.PropertyName == nameof(MainWindowViewModel.IsEditingUsername)
                    && vm.IsEditingUsername)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        UsernameTextBox?.Focus();
                        UsernameTextBox?.SelectAll();
                    });
                }
            };
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is MainWindowViewModel vm && vm.IsEditingUsername)
        {
            if (e.Key == Key.Enter)
            {
                vm.SaveUsernameCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.CancelEditUsernameCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private async Task<string?> PickLocalGameDirectoryAsync()
    {
        var provider = StorageProvider;
        if (provider is null) return null;

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick the SCP:SL game directory (must contain SCPSL.exe)",
            AllowMultiple = false,
        });
        var first = folders.Count > 0 ? folders[0] : null;
        return first?.Path.LocalPath;
    }

    private async void ShowCrashReportDialog(MainWindowViewModel vm)
    {
        var dialog = new CrashReportDialog
        {
            DataContext = vm.CrashDialogViewModel
        };
        await dialog.ShowDialog(this);
        vm.CloseCrashDialogCommand.Execute(null);
    }

    private async void ShowSteamDownloadDialog(MainWindowViewModel vm)
    {
        var dialog = new SteamDownloadDialog
        {
            DataContext = vm.SteamDownloadDialogViewModel
        };
        await dialog.ShowDialog(this);
        vm.CloseSteamDownloadDialogCommand.Execute(null);
    }

    private async void ShowInstallProgressDialog(MainWindowViewModel vm)
    {
        var dialog = new InstallProgressDialog
        {
            DataContext = vm.InstallProgressDialogViewModel
        };
        await dialog.ShowDialog(this);
        vm.CloseInstallProgressDialogCommand.Execute(null);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
    }

    // ===================== Custom chrome =====================

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void OnModCardClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: ModItemViewModel mod }
            && DataContext is MainWindowViewModel vm)
        {
            vm.SelectedMod = mod;
        }
    }

    private void OnVersionRowInstallClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control ctl
            && ctl.Tag is GameVersionItemViewModel entry
            && DataContext is MainWindowViewModel vm)
        {
            vm.InstallGameVersionCommand.Execute(entry);
        }
    }

    private void OnVersionSidebarInstallClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedGameVersion != null)
        {
            vm.InstallGameVersionCommand.Execute(vm.SelectedGameVersion);
        }
    }
}
