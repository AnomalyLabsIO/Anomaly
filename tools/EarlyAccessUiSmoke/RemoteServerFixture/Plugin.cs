using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Anomaly.Server.Api.Content;
using Anomaly.Server.Api.Events.Arguments;
using Anomaly.Server.Api.Events.Handlers;
using Anomaly.Server.Api.Networking;
using Anomaly.ServerApiSmoke;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using LabApi.Features;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;
using UnityEngine;

namespace Anomaly.RemoteScriptingSmoke.Server;

// Opt-in, dedicated-server-only tool. No runtime package includes this assembly.
public sealed class Plugin : Plugin<Config>
{
    private const string Primary = "anomaly_smoke:remote_scripts";
    private const string Owner = "anomaly_smoke:remote_owner_scripts";
    private readonly Dictionary<uint, Proof> _proofs = new Dictionary<uint, Proof>();
    private IDisposable _primary, _owner;
    private string _evidence;
    private int _records;
    private uint _request;
    public override string Name => "Anomaly.RemoteScriptingSmoke";
    public override string Description => "Actual authenticated Lua worker and native API qualification fixture.";
    public override string Author => "AnomalyLabs";
    public override Version Version => new Version(1, 0, 0);
    public override Version RequiredApiVersion => new Version(LabApiProperties.CompiledVersion);
    public override LoadPriority Priority => LoadPriority.Low;

    public override void Enable()
    {
        if (_primary != null || !Config.Enabled) return;
        _evidence = Environment.GetEnvironmentVariable("ANOMALY_REMOTE_SCRIPTING_SERVER_EVIDENCE");
        if (string.IsNullOrWhiteSpace(_evidence)) throw new InvalidOperationException("Remote smoke needs its explicit evidence directory environment variable.");
        _evidence = Path.GetFullPath(_evidence);
        Directory.CreateDirectory(_evidence);
        // Registration captures the exact embedded pack bytes; no disk pack replacement.
        _primary = ContentPacks.RegisterEmbedded(typeof(Plugin).Assembly, "Anomaly.RemoteSmoke.Primary");
        try { _owner = ContentPacks.RegisterEmbedded(typeof(Plugin).Assembly, "Anomaly.RemoteSmoke.Owner"); }
        catch { _primary.Dispose(); _primary = null; throw; }
        AnomalyMessageRegistry.Register(SmokeRequest.Name, () => new SmokeRequest());
        AnomalyMessageRegistry.Register(SmokeResponse.Name, () => new SmokeResponse());
        ClientEvents.Connected += OnConnected;
        ClientEvents.ContentReady += OnContentReady;
        ClientEvents.MessageReceived += OnMessage;
        Record("enabled", new { primary = Primary, owner = Owner,
            primarySourceSha256 = ResourceHash("Anomaly.RemoteSmoke.Primary/scripts/main.lua"),
            ownerSourceSha256 = ResourceHash("Anomaly.RemoteSmoke.Owner/scripts/main.lua") });
    }

    public override void Disable()
    {
        ClientEvents.Connected -= OnConnected;
        ClientEvents.ContentReady -= OnContentReady;
        ClientEvents.MessageReceived -= OnMessage;
        _owner?.Dispose(); _owner = null;
        _primary?.Dispose(); _primary = null;
        _proofs.Clear();
    }

    private void OnConnected(ClientEventArgs ev)
    {
        // Native Connected can follow the content admission transition.
        if (!_proofs.ContainsKey(ev.Player.NetworkId)) _proofs[ev.Player.NetworkId] = new Proof();
        Record("authenticated-connected", new { netId = ev.Player.NetworkId });
    }

    private void OnContentReady(ClientEventArgs ev)
    {
        if (!_proofs.TryGetValue(ev.Player.NetworkId, out var proof)) _proofs[ev.Player.NetworkId] = proof = new Proof();
        proof.ContentReady = true;
        Record("content-ready", new { netId = ev.Player.NetworkId, readiness = CustomItems.GetPlayerReadiness(ev.Player).ToString() });
    }

    private void OnMessage(ClientMessageReceivedEventArgs ev)
    {
        if (!_proofs.TryGetValue(ev.Player.NetworkId, out var proof)) return;
        if (ev.Message is ScriptToServerMessage script && script.PackId == Primary && script.Name == "State")
        {
            if (!script.TryRead<State>(out var state)) throw new InvalidDataException("State schema did not deserialize.");
            proof.Last = state;
            proof.ActivationId = script.ActivationId;
            proof.IndexSha256 = script.IndexSha256;
            proof.Replies++;
            proof.NativeNickname = ev.Player.Nickname;
            var nativePosition = ev.Player.Position;
            proof.PositionMatches = Vector3.Distance(nativePosition, new Vector3((float)state.X, (float)state.Y, (float)state.Z)) < 1f;
            proof.NicknameMatches = state.Nickname == proof.NativeNickname;
            Record("authenticated-state", new { netId = ev.Player.NetworkId, proof });
            return;
        }
        if (ev.Message is not SmokeRequest request || !(request.Command ?? "").StartsWith("remote:", StringComparison.Ordinal)) return;
        try
        {
            string command = request.Command.Substring("remote:".Length);
            if (!proof.ContentReady || CustomItems.GetPlayerReadiness(ev.Player) != ContentReadiness.Activated)
                throw new InvalidOperationException("Remote fixture command before content activation.");
            if (command != "status")
            {
                if (command != "query" && command != "show" && command != "clear" && command != "expire" && command != "churn")
                    throw new InvalidOperationException("Unknown remote fixture command.");
                var query = new Query { RequestId = ++_request, Command = command };
                proof.ExpectedRequestId = query.RequestId;
                if (!ClientMessaging.TrySend(ev.Player, ScriptToClientMessage.Create(Primary, "GetState", query)))
                    throw new InvalidOperationException("Authenticated primary GetState was not admitted.");
                if (command == "show" || command == "clear" || command == "expire")
                    if (!ClientMessaging.TrySend(ev.Player, ScriptToClientMessage.Create(Owner, "GetState", query)))
                        throw new InvalidOperationException("Independent owner GetState was not admitted.");
                Record("authenticated-get-state", new { netId = ev.Player.NetworkId, query });
            }
            ClientMessaging.TrySend(ev.Player, new SmokeResponse { Token = request.Token, Success = true, Details = JsonSerializer.Serialize(proof) });
        }
        catch (Exception error)
        {
            ClientMessaging.TrySend(ev.Player, new SmokeResponse { Token = request.Token, Success = false, Details = JsonSerializer.Serialize(new { error = error.Message }) });
        }
    }

    private static string ResourceHash(string name)
    {
        using (var input = typeof(Plugin).Assembly.GetManifestResourceStream(name))
        using (var hash = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }

    private void Record(string kind, object data)
    {
        if (++_records > 128) throw new InvalidOperationException("Remote fixture evidence record bound exceeded.");
        string json = JsonSerializer.Serialize(new { kind, utc = DateTime.UtcNow, data });
        if (json.Length > 8192) throw new InvalidDataException("Remote fixture evidence record too large.");
        File.AppendAllText(Path.Combine(_evidence, "remote-server-proof.jsonl"), json + Environment.NewLine);
    }

    public sealed class Query { public uint RequestId { get; set; } public string Command { get; set; } }
    public sealed class State
    {
        public uint RequestId { get; set; }
        public string Command { get; set; }
        public bool Enabled { get; set; }
        public uint ReadyCount { get; set; }
        public bool ReadyFrozen { get; set; }
        public string SceneName { get; set; }
        public string Nickname { get; set; }
        public int PlayerId { get; set; }
        public int PlayerCount { get; set; }
        public bool QueryMatches { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public uint ChurnQueries { get; set; }
    }
    public sealed class Proof
    {
        public bool ContentReady { get; set; }
        public uint ExpectedRequestId { get; set; }
        public int Replies { get; set; }
        public ulong ActivationId { get; set; }
        public string IndexSha256 { get; set; }
        public string NativeNickname { get; set; }
        public bool NicknameMatches { get; set; }
        public bool PositionMatches { get; set; }
        public State Last { get; set; }
    }
}

public sealed class Config { public bool Enabled { get; set; } = false; }
