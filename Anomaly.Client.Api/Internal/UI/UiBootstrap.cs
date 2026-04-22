using Anomaly.Client.Api.UI;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using MelonLoader;

namespace Anomaly.Client.Api.Internal.UI;

/// <summary>
/// Ensures the shared <see cref="UiCanvas"/> is created once the game's own
/// canvas has finished bootstrapping. Subscribes to the existing
/// <see cref="ClientEvents.CanvasReady"/> event so we inherit whatever timing
/// the game uses.
/// </summary>
internal static class UiBootstrap
{
    private static bool _hooked;

    internal static void Init()
    {
        if (_hooked) return;
        _hooked = true;

        ClientEvents.CanvasReady += OnCanvasReady;
    }

    private static void OnCanvasReady(CanvasReadyEventArgs args)
    {
        try
        {
            UiCanvas.EnsureCreated();
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"UiBootstrap: EnsureCreated threw: {ex}");
        }
    }
}
