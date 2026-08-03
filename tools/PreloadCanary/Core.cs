using System.Runtime.CompilerServices;
using MelonLoader;

[assembly: MelonInfo(typeof(Anomaly.PreloadCanary.Core), "Anomaly Preload Canary", "1.0.0", "AnomalyLabs", null)]
[assembly: MelonGame("Northwood", "SCPSL")]

namespace Anomaly.PreloadCanary;

internal static class AssemblyCanary
{
    [ModuleInitializer]
    internal static void Initialize() => Record("module-initializer");

    internal static void Record(string phase)
    {
        var marker = Environment.GetEnvironmentVariable("ANOMALY_PRELOAD_CANARY_MARKER");
        if (string.IsNullOrWhiteSpace(marker)) return;
        File.AppendAllText(marker, phase + Environment.NewLine);
    }
}

public sealed class Core : MelonMod
{
    static Core() => AssemblyCanary.Record("static-constructor");

    public override void OnInitializeMelon() => AssemblyCanary.Record("melon-initialize");
}
