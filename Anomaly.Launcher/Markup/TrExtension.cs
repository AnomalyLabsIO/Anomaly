using Anomaly.Launcher.Services.Localization;
using Avalonia.Markup.Xaml;

namespace Anomaly.Launcher.Markup;

/// <summary>
/// Avalonia MarkupExtension for translation lookups in AXAML.
/// Usage: {l:Tr launcher.mainwindow.launch_vanilla} where xmlns:l="using:Anomaly.Launcher.Markup".
/// Returns a static string (no live locale switching in v1 — change requires app restart).
/// </summary>
public class TrExtension : MarkupExtension
{
    public string? Key { get; set; }

    public TrExtension() { }

    public TrExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Key == null ? string.Empty : Tr.Get(Key);
    }
}
