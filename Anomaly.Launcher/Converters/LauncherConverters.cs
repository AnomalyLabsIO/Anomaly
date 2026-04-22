using System.Globalization;
using Anomaly.Launcher.ViewModels;
using Avalonia.Data.Converters;

namespace Anomaly.Launcher.Converters;

/// <summary>Returns <c>enabledOpacity</c> when bound bool is true, else <c>disabledOpacity</c>.</summary>
public sealed class EnabledToOpacityConverter : IValueConverter
{
    public double EnabledOpacity { get; set; } = 1.0;
    public double DisabledOpacity { get; set; } = 0.55;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? EnabledOpacity : DisabledOpacity;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Compares a bound <see cref="LauncherTab"/> to a string parameter.</summary>
public sealed class TabEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not LauncherTab tab || parameter is not string p) return false;
        return p.ToLowerInvariant() switch
        {
            "installed" => tab == LauncherTab.Installed,
            "public" => tab == LauncherTab.PublicRepo,
            "versions" => tab == LauncherTab.GameVersions,
            "settings" => tab == LauncherTab.Settings,
            _ => false,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
