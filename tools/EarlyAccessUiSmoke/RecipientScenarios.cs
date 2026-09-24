using System.Text.Json;
using Anomaly.Client.Api.Networking;
using Anomaly.ServerApiSmoke;
using Anomaly.Shared.Networking;
using UnityEngine;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private readonly int[] _recipientCounts = new int[3];
    private string _recipientRole;

    private void EnqueueRecipients(string server, string role)
    {
        _recipientRole = role;
        AnomalyMessaging.MessageReceived += OnRecipientMessage;
        Add("recipients-nickname", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            {
                Input().SetTextWithoutNotify("Recipient " + role);
                Submit();
                Require(!IsVisible(), "Fresh recipient nickname was saved");
            }
        });
        Add("recipients-connect", () =>
        {
            _sawContentPrompt = false;
            _contentDecisionAt = 0;
            ApiClient.MainMenu.ChangeMenu(0);
            ApiClient.Connect(server);
        });
        AddWait("recipients-ready", () => PumpServerApiConnection(1, approve: false, deliberation: 0), 180);
        AddSmokeRequest("recipients-join", "recipients:join:" + role, _ => { });
        if (role == "sender")
        {
            AddRecipientPoll("recipients-pair", json => json.GetProperty("registered").GetInt32() == 2);
            AddSmokeRequest("recipients-start", "recipients:start", _ => { });
        }
        AddRecipientPoll("recipients-complete", json =>
        {
            if (!json.GetProperty("complete").GetBoolean()) return false;
            File.WriteAllText(Path.Combine(_output, "server-recipients.json"), json.GetRawText());
            return true;
        });
        Add("recipients-assert", () =>
        {
            Require(_recipientCounts[0] == (role == "sender" ? 1 : 0), "Single-target inclusion and exclusion");
            Require(_recipientCounts[1] == 1, "Broadcast reaches each authenticated client once");
            Require(_recipientCounts[2] == (role == "observer" ? 1 : 0), "Excluded broadcast omits the specified player");
            File.WriteAllText(Path.Combine(_output, "client-recipients.json"), JsonSerializer.Serialize(new { role, counts = _recipientCounts }));
            Capture("recipients-" + role);
            ApiClient.Disconnect();
        });
        AddWait("recipients-disconnected", () => !ApiClient.IsConnected && ApiClient.MainMenu != null, 60);
        Add("recipients-result", () =>
        {
            File.WriteAllText(Path.Combine(_output, "result.txt"), "Two-client recipients " + role + " passed: single, broadcast, exclusion with per-client acknowledgements.\n");
            Application.Quit();
        });
    }

    private void OnRecipientMessage(IAnomalyMessage message)
    {
        if (message is not SmokeResponse response || response.Token > -1001 || response.Token < -1003) return;
        int index = -1001 - response.Token;
        _recipientCounts[index]++;
        File.AppendAllText(Path.Combine(_output, "recipient-deliveries.jsonl"), JsonSerializer.Serialize(new
        {
            role = _recipientRole, token = response.Token, response.Success, response.Details,
            utc = DateTime.UtcNow.ToString("O"),
        }) + "\n");
        AnomalyMessaging.Send(new SmokeRequest { Token = ++_requestToken, Command = "recipients:ack:" + response.Token });
    }

    private void AddRecipientPoll(string name, Func<JsonElement, bool> predicate)
    {
        int token = 0;
        double next = 0;
        AddWait(name, () =>
        {
            if (token != 0)
            {
                if (!_smokeResponses.TryGetValue(token, out var response)) return false;
                _smokeResponses.Remove(token);
                Require(response.Success, name + ": " + response.Details);
                using var json = JsonDocument.Parse(response.Details);
                Require(json.RootElement.GetProperty("failure").ValueKind == JsonValueKind.Null, "No unexpected recipient deliveries");
                if (predicate(json.RootElement)) return true;
                token = 0;
            }
            if (Time.realtimeSinceStartupAsDouble < next) return false;
            next = Time.realtimeSinceStartupAsDouble + 0.5;
            token = ++_requestToken;
            AnomalyMessaging.Send(new SmokeRequest { Token = token, Command = "recipients:status" });
            return false;
        }, 180);
    }
}
