using System.Text.Json;
using Anomaly.Client.Api.Networking;
using Anomaly.ServerApiSmoke;
using Anomaly.Shared.Networking.Messages;
using UnityEngine;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private ulong _previousFacilityGeneration;

    private void EnqueueFacilityMatrix(string server)
    {
        Add("facility-nickname", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            {
                Input().SetTextWithoutNotify("Facility Smoke");
                Submit();
            }
            ApiClient.MainMenu.ChangeMenu(0);
            ApiClient.Connect(server);
        });
        AddWait("facility-pristine-ready", () => PumpServerApiConnection(1, false, 0), 150);
        AddFacilityCapture(1, "pristine");
        Add("facility-late-disconnect", () => { Call("HideAll"); ApiClient.Disconnect(); });
        AddWait("facility-late-menu", () => !ApiClient.IsConnected && ApiClient.MainMenu != null, 60);
        Add("facility-late-reconnect", () => { ApiClient.MainMenu.ChangeMenu(0); ApiClient.Connect(server); });
        AddWait("facility-late-ready", () => PumpServerApiConnection(2, false, 0), 150);
        AddFacilityCapture(2, "late");
        string[] scenarios = { "noop", "lcz", "hcz", "entrance", "atlas", "atlas", "planned-noop", "planned-move", "planned-remove", "planned-checkpoint", "planned-remove" };
        for (int index = 0; index < scenarios.Length; index++)
        {
            int cycle = index + 3;
            string scenario = scenarios[index];
            Add("facility-restart-" + cycle, () =>
            {
                _sawContentPrompt = false;
                _previousFacilityGeneration = CurrentFacilitySnapshot()?.GenerationId ?? 0;
                AnomalyMessaging.Send(new SmokeRequest { Token = ++_requestToken, Command = "facility-restart:" + scenario });
            });
            AddWait("facility-next-ready-" + cycle, () =>
            {
                if (!PumpServerApiConnection(cycle, false, 0)) return false;
                var recipe = Type.GetType("Anomaly.Client.Internal.Map.FacilityRecipeClient, Anomaly.Client", true);
                return (bool)recipe.GetProperty("IsReady", All).GetValue(null) &&
                    CurrentFacilitySnapshot()?.GenerationId > _previousFacilityGeneration;
            }, 180);
            AddFacilityCapture(cycle, scenario);
        }
        Add("facility-planned-late-disconnect", () => { Call("HideAll"); ApiClient.Disconnect(); });
        AddWait("facility-planned-late-menu", () => !ApiClient.IsConnected && ApiClient.MainMenu != null, 60);
        Add("facility-planned-late-reconnect", () => { ApiClient.MainMenu.ChangeMenu(0); ApiClient.Connect(server); });
        AddWait("facility-planned-late-ready", () => PumpServerApiConnection(14, false, 0), 150);
        AddFacilityCapture(14, "late-planned-remove");
        Add("facility-matrix-complete", () =>
        {
            File.WriteAllText(Path.Combine(_output, "result.txt"), "Facility matrix generated pristine, late reconnect, legacy recipes, whole-layout no-op/move/removal/checkpoint, and repeated atlas/removal rounds. Compare saved client/server descriptors.\n");
            ApiClient.Disconnect();
            Application.Quit();
        });
    }

    private void AddFacilityCapture(int cycle, string scenario)
    {
        AddSmokePoll("facility-content-declined-" + cycle, "report", json =>
        {
            PumpServerApiConnection(cycle, false, 0);
            string readiness = json.GetProperty("readiness").GetString();
            if (readiness != "Declined") return false;
            File.WriteAllText(Path.Combine(_output, "facility-content-" + cycle + ".json"), json.GetRawText());
            return true;
        });
        Add("facility-capture-" + cycle, () =>
        {
            SaveFacilityEvidence(cycle);
            File.WriteAllText(Path.Combine(_output, "facility-scenario-" + cycle + ".txt"), scenario);
            Capture("facility-" + cycle + "-" + scenario);
        });
        AddSmokeRequest("facility-report-" + cycle, "report", json =>
        {
            File.WriteAllText(Path.Combine(_output, "facility-server-report-" + cycle + ".json"), json.GetRawText());
            File.WriteAllText(Path.Combine(_output, "facility-contents-" + cycle + ".json"), FacilityContents.Capture());
        });
    }

    private static FacilitySnapshotMessage CurrentFacilitySnapshot()
    {
        var recipe = Type.GetType("Anomaly.Client.Internal.Map.FacilityRecipeClient, Anomaly.Client", true);
        return (FacilitySnapshotMessage)recipe.GetProperty("CurrentSnapshot", All).GetValue(null);
    }
}
