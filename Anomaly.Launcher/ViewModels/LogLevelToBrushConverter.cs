using System.Globalization;
using Anomaly.InstallerCore;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Anomaly.Launcher.ViewModels;

public sealed class LogLevelToBrushConverter : IValueConverter
{
    private static readonly IBrush Info = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private static readonly IBrush Success = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly IBrush Warning = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
    private static readonly IBrush Error = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            InstallerLogLevel.Success => Success,
            InstallerLogLevel.Warning => Warning,
            InstallerLogLevel.Error => Error,
            _ => Info
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
