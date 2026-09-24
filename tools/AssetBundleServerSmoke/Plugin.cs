using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Anomaly.Server.Api.Assets;
using Anomaly.Server.Api.Content;
using Anomaly.Server.Api.Events.Arguments;
using Anomaly.Server.Api.Events.Handlers;
using Anomaly.Server.Api.Networking;
using Anomaly.ServerApiSmoke;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;
using PlayerRoles;
using UnityEngine;
using Announcer = Anomaly.Server.Api.Features.Announcer;

namespace Anomaly.AssetBundleServerSmoke;

/// <summary>Opt-in ordinary LabAPI fixture. Never packaged or deployed to an ordinary server.</summary>
public sealed class Plugin : Plugin<Config>
{
    public const string ItemKey = "fixture:coin";
    public const string WordName = "ANOMALY_BUNDLE_TONE";
    private static readonly AssetAddress Texture = AssetAddress.Parse("mods/fixture/textures/checker");
    private static readonly AssetAddress Sprite = AssetAddress.Parse("mods/fixture/sprites/checker");
    private static readonly AssetAddress Audio = AssetAddress.Parse("mods/fixture/audio/tone");
    private static readonly AssetAddress Visual = AssetAddress.Parse("mods/fixture/visuals/quad");
    private DecorativeVisualScope _scope;
    private string _evidence;
    private bool _initialized;
    private uint _visualId;
    private float _duration;
    private int _readyCount;
    private int _roundCount;
    private readonly Dictionary<uint, int> _requestedFiles = new Dictionary<uint, int>();
    public override string Name => "Anomaly AssetBundle Server Smoke";
    public override string Description => "Private loopback AssetBundle integration fixture.";
    public override string Author => "AnomalyLabs";
    public override Version Version => new Version(1, 0, 0);
    public override Version RequiredApiVersion => new Version(1, 1, 7);
    public override LoadPriority Priority => LoadPriority.Low;

    public override void Enable()
    {
        _evidence = Environment.GetEnvironmentVariable("ANOMALY_BUNDLE_SERVER_SMOKE_DIR");
        if (string.IsNullOrWhiteSpace(_evidence)) return;
        Directory.CreateDirectory(_evidence);
        CustomItems.Register(new CustomItemDefinition { Key = ItemKey, BaseTemplate = ItemType.Coin,
            DisplayName = "Bundle fixture coin", Description = "Offline Sprite icon fixture", IconAddress = Sprite });
        AnomalyMessageRegistry.Register(SmokeRequest.Name, () => new SmokeRequest());
        AnomalyMessageRegistry.Register(SmokeResponse.Name, () => new SmokeResponse());
        ClientEvents.Connected += Connected;
        ClientEvents.ContentReady += Ready;
        ClientEvents.MessageReceived += Message;
        ServerEvents.RoundRestarted += RoundRestarted;
        StaticUnityMethods.OnUpdate += Tick;
        Record("enabled", new { fixture = true });
    }
    public override void Disable()
    {
        if (string.IsNullOrWhiteSpace(_evidence)) return;
        ClientEvents.Connected -= Connected;
        ClientEvents.ContentReady -= Ready;
        ClientEvents.MessageReceived -= Message;
        ServerEvents.RoundRestarted -= RoundRestarted;
        StaticUnityMethods.OnUpdate -= Tick;
        _scope?.Dispose(); _scope = null; _visualId = 0;
        Record("disabled", new { disposed = true });
    }
    private void Tick()
    {
        if (_initialized || !AssetRegistry.IsReady) return;
        try
        {
            if (!AssetRegistry.Contains(Visual) || !AssetRegistry.Contains(Texture) || !AssetRegistry.Contains(Sprite)) return;
            _duration = Announcer.AddWord(WordName, Audio).DurationSeconds;
            _scope = new DecorativeVisualScope("fixture");
            _visualId = _scope.Spawn(Visual, new Vector3(0, -1000, 0), Quaternion.identity, Vector3.one);
            _initialized = true;
            Record("assets-ready", new { addresses = AssetRegistry.All().Count, audioDuration = _duration, visualId = _visualId, fixtureTriangles = 2 });
        }
        catch (Exception exception) { _initialized = true; Record("init-failed", new { error = exception.ToString() }); }
    }
    private void Connected(ClientEventArgs ev) => Record("connected", new { netId = ev.Player.NetworkId,
        readiness = CustomItems.GetPlayerReadiness(ev.Player).ToString(), prematureGrant = CustomItems.TryGiveItem(ev.Player, ItemKey, out _) });
    private void Ready(ClientEventArgs ev) { _readyCount++; Record("activated", Status(ev.Player)); }
    private void RoundRestarted() { _roundCount++; _visualId = 0; Record("round-restarted", new { rounds = _roundCount }); }
    private void Message(ClientMessageReceivedEventArgs ev)
    {
        if (ev.Message is ContentCatalogDecisionMessage decision)
        {
            Record("content-decision", new { netId = ev.Player.NetworkId, decision = decision.Decision.ToString(),
                catalogHash = Hex(decision.CatalogSha256), manifestHash = Hex(decision.ManifestSha256) });
            return;
        }
        if (ev.Message is ContentCatalogAckMessage acknowledgement)
        {
            // Failed acknowledgements revoke admission inside the core before this
            // public event. The corresponding core rejection log is the witness.
            Record("content-ack", new { netId = ev.Player.NetworkId, status = acknowledgement.Status.ToString(),
                failedKeys = acknowledgement.FailedKeys, catalogHash = Hex(acknowledgement.CatalogSha256),
                manifestHash = Hex(acknowledgement.ManifestSha256) });
            return;
        }
        if (ev.Message is FileRequestMessage transfer)
        {
            _requestedFiles[ev.Player.NetworkId] = transfer.RequestedHashes.Count;
            Record("file-request", new { netId = ev.Player.NetworkId, sessionId = transfer.SessionId, requestedFiles = transfer.RequestedHashes.Count });
            return;
        }
        if (ev.Message is not SmokeRequest request) return;
        try
        {
            string[] args = (request.Command ?? "status").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string command = args.Length == 0 ? "status" : args[0];
            bool restart = false;
            if (command != "status" && CustomItems.GetPlayerReadiness(ev.Player) != ContentReadiness.Activated)
                throw new InvalidOperationException("The exact pack catalog has not activated.");
            switch (command)
            {
                case "status": break;
                case "prepare": ev.Player.Role = RoleTypeId.ClassD; break;
                case "spawn":
                case "respawn":
                    if (_scope == null) _scope = new DecorativeVisualScope("fixture");
                    if (_visualId != 0) _scope.Despawn(_visualId);
                    _visualId = _scope.Spawn(Visual, ev.Player.Position + Vector3.forward * 3, Quaternion.identity, Vector3.one); break;
                case "update":
                    _scope.Update(_visualId, ev.Player.Position + Vector3.forward * 4 + Vector3.up, Quaternion.Euler(0, 20, 0), Vector3.one * 1.5f); break;
                case "despawn": _scope.Despawn(_visualId); _visualId = 0; break;
                case "dispose": _scope?.Dispose(); _scope = null; _visualId = 0; break;
                case "grant":
                    if (!CustomItems.TryGiveItem(ev.Player, ItemKey, out Item item)) throw new InvalidOperationException("Item grant failed.");
                    ev.Player.CurrentItem = item; break;
                case "texture":
                    if (args.Length != 4) throw new ArgumentException("texture <netId> <base64-component-path-or-dash> <material-index>");
                    AssetOverrides.SetTexture(uint.Parse(args[1], CultureInfo.InvariantCulture), args[2] == "-" ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(args[2])), int.Parse(args[3], CultureInfo.InvariantCulture), Texture); break;
                case "clear": AssetOverrides.ClearAll(uint.Parse(args[1], CultureInfo.InvariantCulture)); break;
                case "playtone": LabApi.Features.Wrappers.Announcer.Message(WordName, "Bundle fixture tone", false); break;
                case "round": restart = true; break;
                default: throw new ArgumentException("Unknown fixture command: " + command);
            }
            object status = Status(ev.Player);
            Record(command, status);
            ClientMessaging.Send(ev.Player, new SmokeResponse { Token = request.Token, Success = true, Details = JsonSerializer.Serialize(status) });
            if (restart) Round.RestartSilently();
        }
        catch (Exception exception)
        {
            var error = new { error = exception.ToString() }; Record("request-failed", error);
            ClientMessaging.TrySend(ev.Player, new SmokeResponse { Token = request.Token, Success = false, Details = JsonSerializer.Serialize(error) });
        }
    }
    private object Status(Player player)
    {
        CustomItems.TryGetItemId(ItemKey, out ItemType itemId);
        int requestedFiles = _requestedFiles.TryGetValue(player.NetworkId, out int count) ? count : -1;
        return new { netId = player.NetworkId, readiness = CustomItems.GetPlayerReadiness(player).ToString(), readyCount = _readyCount,
            rounds = _roundCount, visualId = _visualId, scopeDisposed = _scope == null, assetReady = _initialized,
            itemId = (int)itemId, audioDuration = _duration, fixtureTriangles = 2, requestedFiles };
    }
    private void Record(string name, object details) => File.AppendAllText(Path.Combine(_evidence, "events.jsonl"),
        JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow.ToString("O"), name, details }) + Environment.NewLine);
    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
}
public sealed class Config { }
