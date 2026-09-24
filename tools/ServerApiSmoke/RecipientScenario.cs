using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Anomaly.Server.Api.Networking;
using LabApi.Features.Wrappers;

namespace Anomaly.ServerApiSmoke;

internal static class RecipientScenario
{
    internal const int SingleToken = -1001;
    internal const int AllToken = -1002;
    internal const int ExceptToken = -1003;
    private static readonly Dictionary<string, Player> Players = new Dictionary<string, Player>(StringComparer.Ordinal);
    private static readonly Dictionary<string, int[]> Counts = new Dictionary<string, int[]>(StringComparer.Ordinal);
    private static DateTime _started;
    private static string _evidence;
    private static string _failure;

    internal static void Initialize(string evidence)
    {
        Players.Clear();
        Counts.Clear();
        _started = default;
        _failure = null;
        _evidence = evidence;
    }

    internal static string Handle(Player player, string[] command)
    {
        if (command.Length < 2) throw new ArgumentException("recipients:<join|start|ack|status>");
        string role = Role(player);
        switch (command[1])
        {
            case "join":
                if (command.Length != 3 || (command[2] != "sender" && command[2] != "observer"))
                    throw new ArgumentException("recipients:join:<sender|observer>");
                if (Players.TryGetValue(command[2], out Player existing) && existing.NetworkId != player.NetworkId)
                    throw new InvalidOperationException("Recipient role already belongs to another client.");
                if (role != null && role != command[2]) throw new InvalidOperationException("A client cannot fill both recipient roles.");
                Players[command[2]] = player;
                if (!Counts.ContainsKey(command[2])) Counts.Add(command[2], new int[3]);
                break;
            case "start":
                if (role != "sender" || Players.Count != 2 || _started != default)
                    throw new InvalidOperationException("Exactly two distinct registered clients are required for one sender-started batch.");
                _started = DateTime.UtcNow;
                ClientMessaging.Send(Players["sender"], Notice(SingleToken, "single"));
                ClientMessaging.SendToAll(Notice(AllToken, "all"));
                ClientMessaging.SendToAllExcept(Players["sender"], Notice(ExceptToken, "except-sender"));
                break;
            case "ack":
                if (role == null || command.Length != 3 || _started == default)
                    throw new InvalidOperationException("Acknowledgement requires a registered active recipient.");
                int token = int.Parse(command[2], CultureInfo.InvariantCulture);
                int index = token == SingleToken ? 0 : token == AllToken ? 1 : token == ExceptToken ? 2 : -1;
                if (index < 0) throw new ArgumentOutOfRangeException(nameof(token));
                Counts[role][index]++;
                int expected = Expected(role, index);
                if (Counts[role][index] > expected) _failure = role + " received unexpected or duplicate delivery " + token;
                break;
            case "status": break;
            default: throw new ArgumentException("Unknown recipient operation.");
        }
        string status = Status();
        File.WriteAllText(Path.Combine(_evidence, "recipients.json"), status);
        return status;
    }

    private static SmokeResponse Notice(int token, string kind) => new SmokeResponse
    {
        Token = token, Success = true, Details = "{\"recipientProbe\":\"" + kind + "\"}"
    };

    private static string Role(Player player)
    {
        foreach (var pair in Players)
            if (pair.Value.NetworkId == player.NetworkId) return pair.Key;
        return null;
    }

    private static int Expected(string role, int index) => index == 1 || (role == "sender" ? index == 0 : index == 2) ? 1 : 0;

    private static string Status()
    {
        bool complete = Players.Count == 2 && _started != default && _failure == null && (DateTime.UtcNow - _started).TotalSeconds >= 3;
        foreach (var pair in Counts)
            for (int index = 0; index < 3; index++) complete &= pair.Value[index] == Expected(pair.Key, index);
        return JsonSerializer.Serialize(new
        {
            registered = Players.Count,
            started = _started != default,
            complete,
            failure = _failure,
            senderNetId = Players.TryGetValue("sender", out Player sender) ? sender.NetworkId : 0,
            observerNetId = Players.TryGetValue("observer", out Player observer) ? observer.NetworkId : 0,
            sender = Counts.TryGetValue("sender", out int[] senderCounts) ? senderCounts : new int[3],
            observer = Counts.TryGetValue("observer", out int[] observerCounts) ? observerCounts : new int[3],
            exclusionObservationSeconds = _started == default ? 0 : (DateTime.UtcNow - _started).TotalSeconds,
        });
    }
}
