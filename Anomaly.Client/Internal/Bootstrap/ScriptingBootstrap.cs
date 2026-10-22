using Anomaly.Client.Scripting;
using MelonLoader;

namespace Anomaly.Client.Internal.Bootstrap;

/// <summary>Installs the inert scripting host. Remote source is never loaded here.</summary>
internal static class ScriptingBootstrap
{
    private static ScriptRuntime _runtime;

    internal static void Initialize()
    {
        if (_runtime != null) return;
        var runtime = new ScriptRuntime(Report);
        try
        {
            runtime.DiscoverApi();
            _runtime = runtime;
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    private static void Report(string message) => MelonLogger.Warning("Scripting: " + message);

    internal static void Tick() => _runtime?.Tick();

    internal static void RevokeAll() => _runtime?.RevokeAll();

    internal static void Shutdown()
    {
        var runtime = _runtime;
        _runtime = null;
        runtime?.Dispose();
    }
}
