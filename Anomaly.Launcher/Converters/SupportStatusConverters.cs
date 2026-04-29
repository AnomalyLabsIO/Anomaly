using System.Globalization;
using Anomaly.Launcher.Models.Steam;
using Anomaly.Launcher.Services.Localization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Anomaly.Launcher.Converters;

/// <summary>
/// Maps <see cref="SupportStatus"/> to a tinted row background for the version-picker grid.
/// Uses translucent colors so selection highlight still shows through.
/// </summary>
public sealed class SupportStatusToBrushConverter : IValueConverter
{
    private static readonly IBrush GreenTint = new SolidColorBrush(Color.FromArgb(0x33, 0x4C, 0xAF, 0x50));
    private static readonly IBrush OrangeTint = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x98, 0x00));
    private static readonly IBrush RedTint = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x52, 0x52));
    private static readonly IBrush NeutralTint = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SupportStatus s
            ? s switch
            {
                SupportStatus.FullySupported => GreenTint,
                SupportStatus.Patchable => OrangeTint,
                SupportStatus.Unsupported => RedTint,
                _ => NeutralTint,
            }
            : NeutralTint;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps <see cref="SupportStatus"/> to a short localized label for the Supported column.
/// </summary>
public sealed class SupportStatusToLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SupportStatus s
            ? s switch
            {
                SupportStatus.FullySupported => Tr.Get(TrKeys.SteamDl_Status_Supported),
                SupportStatus.Patchable => Tr.Get(TrKeys.SteamDl_Status_Patchable),
                SupportStatus.Unsupported => Tr.Get(TrKeys.SteamDl_Status_Unsupported),
                _ => Tr.Get(TrKeys.SteamDl_Status_Unknown),
            }
            : Tr.Get(TrKeys.SteamDl_Status_Unknown);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
