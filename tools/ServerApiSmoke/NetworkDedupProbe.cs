using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;

namespace Anomaly.ServerApiSmoke;

// Test-only observation before the stock console rate limiter. Inert without the
// private fixture's evidence directory; never deployed in runtime packages.
internal static class NetworkDedupProbe
{
    internal const string Prefix = "anomaly_network_probe:";
    private static readonly List<string> Received = new List<string>();
    private static Harmony _harmony;
    private static string _evidence;

    internal static void Initialize(string evidence)
    {
        _evidence = evidence;
        Received.Clear();
        _harmony = new Harmony("anomaly.smoke.network-dedup");
        _harmony.Patch(AccessTools.DeclaredMethod(typeof(GameConsoleTransmission), "ServerHandleCommand"),
            prefix: new HarmonyMethod(typeof(NetworkDedupProbe).GetMethod(nameof(Observe), BindingFlags.NonPublic | BindingFlags.Static)));
    }

    internal static void Shutdown()
    {
        _harmony?.UnpatchAll("anomaly.smoke.network-dedup");
        _harmony = null;
        _evidence = null;
    }

    private static bool Observe(EncryptedChannelManager.EncryptedMessage content)
    {
        string value = content.ToString();
        if (_evidence == null || !value.StartsWith(Prefix, StringComparison.Ordinal)) return true;
        Received.Add(value.Substring(Prefix.Length));
        File.WriteAllText(Path.Combine(_evidence, "network-dedup-received.json"), Report());
        return false;
    }

    internal static string Report() => JsonSerializer.Serialize(new { count = Received.Count, messages = Received.ToArray() });
}
