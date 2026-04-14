using Avalonia.Controls;
using Avalonia.Input;
using Anomaly.Launcher.ViewModels;

namespace Anomaly.Launcher.Views;

public partial class CrashReportDialog : Window
{
    public CrashReportDialog()
    {
        InitializeComponent();

        CloseButton.Click += (_, _) => Close();

        CopyButton.Click += async (_, _) =>
        {
            if (DataContext is CrashReportDialogViewModel vm && Clipboard != null)
            {
                await Clipboard.SetTextAsync(vm.SelectedReportDetail);
            }
        };

        OpenFolderButton.Click += (_, _) =>
        {
            // Handled via the parent window's ViewModel command
            if (Owner is MainWindow { DataContext: MainWindowViewModel mainVm })
            {
                mainVm.OpenCrashReportsFolderCommand.Execute(null);
            }
        };
    }
}
