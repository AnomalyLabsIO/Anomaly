using System.Diagnostics;
using Anomaly.Client.API.Features;
using Anomaly.Client.Internal.Bootstrap;
using Anomaly.Client.Internal.Wrappers;
using MelonLoader;
using UnityEngine;
using ClientApi = Anomaly.Client.API.Features.Client;

namespace Anomaly.Client.Diagnostics;

public static class DiagnosticCollector
{
    private static volatile GameStateSnapshot _lastKnownState = new();
    private static CrashSystemInfo _cachedSystemInfo;

    public static void CacheSystemInfo()
    {
        try
        {
            _cachedSystemInfo = new CrashSystemInfo
            {
                OperatingSystem = Environment.OSVersion.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                CpuType = SystemInfo.processorType,
                SystemMemoryMb = SystemInfo.systemMemorySize,
                GpuName = SystemInfo.graphicsDeviceName,
                UnityVersion = Application.unityVersion,
                MelonLoaderVersion = MelonLoader.Properties.BuildInfo.Version ?? "unknown",
                ManagedMemoryBytes = GC.GetTotalMemory(false)
            };
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"DiagnosticCollector: Failed to cache system info: {ex.Message}");
            _cachedSystemInfo = new CrashSystemInfo
            {
                OperatingSystem = Environment.OSVersion.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                ManagedMemoryBytes = GC.GetTotalMemory(false)
            };
        }
    }

    public static void RefreshGameState()
    {
        try
        {
            _lastKnownState = new GameStateSnapshot
            {
                CurrentScene = ClientApi.CurrentSceneName,
                IsConnected = ClientApi.IsConnected,
                ServerIp = ClientApi.IsConnected ? ClientApi.ServerIp : null,
                PlayerCount = PlayerRegistry.PlayerList?.Count ?? 0,
                IsClientReady = ClientRuntime.IsClientReady
            };
        }
        catch
        {
            // Silently fail -- game state may not be available yet
        }
    }

    public static CrashSystemInfo GetSystemInfo()
    {
        var info = _cachedSystemInfo ?? new CrashSystemInfo();
        info.ManagedMemoryBytes = GC.GetTotalMemory(false);
        return info;
    }

    public static GameStateSnapshot GetGameState()
    {
        return _lastKnownState ?? new GameStateSnapshot();
    }

    public static string[] GetLoadedMods()
    {
        try
        {
            var mods = MelonMod.RegisteredMelons;
            return mods?.Select(m => $"{m.Info.Name} v{m.Info.Version}").ToArray()
                   ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
