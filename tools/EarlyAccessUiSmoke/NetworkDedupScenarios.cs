using System.Text;
using System.Text.Json;
using Anomaly.Client.Api.Features;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private void EnqueueNetworkDedupSmoke(string server)
    {
        string[] expected = { "echo alpha done", "echo bravo done", "echo alpha done", "repeat same done", "repeat same done",
            "byte alpha done", "byte bravo done", "byte alpha done" };
        Add("network-nickname", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            { Input().SetTextWithoutNotify("Network Smoke"); Submit(); }
        });
        Add("network-connect", () => { _contentDecisionAt = 0; ApiClient.MainMenu.ChangeMenu(0); ApiClient.Connect(server); });
        AddWait("network-ready", () => PumpServerApiConnection(1, true, 0), 150);
        Add("network-single-frame-sends", () =>
        {
            var manager = LocalPlayer.Instance.ReferenceHub.encryptedChannelManager;
            int frame = Time.frameCount;
            string owner = Type.GetType("Anomaly.Client.Core, Anomaly.Client", true).Assembly.GetName().Name;
            var patchCounts = new List<object>();
            foreach (var method in typeof(EncryptedChannelManager).GetMethods().Where(m => m.Name == "TrySendMessageToServer"))
            {
                var info = HarmonyLib.Harmony.GetPatchInfo(method);
                int count = info?.Prefixes.Count(p => p.PatchMethod.DeclaringType?.Assembly.GetName().Name == owner) ?? 0;
                Require(count == 1, "Exactly one Anomaly prefix on " + method);
                patchCounts.Add(new { method = method.ToString(), count });
            }
            for (int i = 0; i < expected.Length; i++)
            {
                string value = "anomaly_network_probe:" + expected[i];
                bool sent = i < 5
                    ? manager.TrySendMessageToServer(value, EncryptedChannelManager.EncryptedChannel.GameConsole)
                    : manager.TrySendMessageToServer((Il2CppStructArray<byte>)Encoding.UTF8.GetBytes(value), EncryptedChannelManager.EncryptedChannel.GameConsole);
                Require(sent, "Native send accepted " + i);
            }
            Require(Time.frameCount == frame, "All collision/repeated sends share one Unity frame");
            File.WriteAllText(Path.Combine(_output, "network-dedup-sends.json"), JsonSerializer.Serialize(new { frame, expected, patchCounts }));
        });
        AddSmokeRequest("network-wire-report", "network-report", json =>
        {
            string[] received = json.GetProperty("messages").EnumerateArray().Select(x => x.GetString()).ToArray();
            Require(received.SequenceEqual(expected), "Server receives every intentional send exactly once and in order");
            File.WriteAllText(Path.Combine(_output, "network-dedup-result.json"), json.GetRawText());
        });
        Add("network-disconnect", () => { _disconnectedEventsBeforeStop = _disconnectedEvents; ApiClient.Disconnect(); });
        AddWait("network-disconnected", () => !ApiClient.IsConnected && !ApiClient.IsClientReady, 40);
        Add("network-complete", () =>
        {
            Require(_readyEvents == 1 && _disconnectedEvents == _disconnectedEventsBeforeStop + 1,
                "One lifecycle publication for Ready and the explicit disconnect; handshake attempts counted separately");
            File.WriteAllText(Path.Combine(_output, "result.txt"), "Network dedup smoke passed: 8 same-frame native sends received exactly once; one Ready/Disconnected lifecycle event.\n");
            Application.Quit();
        });
    }
}
