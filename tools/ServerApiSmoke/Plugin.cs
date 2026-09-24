using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Anomaly.Server.Api.Assets;
using Anomaly.Server.Api.Content;
using Anomaly.Server.Api.Events.Arguments;
using Anomaly.Server.Api.Events.Handlers;
using Anomaly.Server.Api.Features;
using Anomaly.Server.Api.Networking;
using Anomaly.Shared.Capabilities;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using Logger = LabApi.Features.Console.Logger;
using Announcer = Anomaly.Server.Api.Features.Announcer;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;
using PlayerRoles;
using UnityEngine;

namespace Anomaly.ServerApiSmoke;

/// <summary>Opt-in ordinary LabAPI plugin. Never part of a release package.</summary>
public sealed class Plugin : Plugin<Config>
{
    public const string ItemKey = "anomaly_smoke:medkit";
    public const string WordName = "anomaly_smoke_tone";
    private const string MotionOwner = "anomaly-smoke";
    private static readonly CapabilityDescriptor Requirement = new CapabilityDescriptor("anomaly-smoke:client", 1, 0);
    private readonly Dictionary<uint, DateTime> _connected = new Dictionary<uint, DateTime>();
    private readonly Dictionary<uint, Item> _granted = new Dictionary<uint, Item>();
    private readonly Dictionary<uint, uint> _motionSequences = new Dictionary<uint, uint>();
    private readonly Dictionary<uint, HashSet<uint>> _textureTargets = new Dictionary<uint, HashSet<uint>>();
    private string _evidence;
    private int _contentReadyCount;
    private int _usedCount;
    private int _motionCount;
    private bool _enabled;
    public override string Name => "Anomaly Server API Smoke";
    public override string Description => "Isolated, opt-in qualification fixture for Anomaly's public LabAPI surface.";
    public override string Author => "AnomalyLabs";
    public override Version Version => new Version(1, 0, 0);
    public override Version RequiredApiVersion => new Version(1, 1, 7);
    public override LoadPriority Priority => LoadPriority.Low;

    public override void Enable()
    {
        _evidence = Environment.GetEnvironmentVariable("ANOMALY_SERVER_SMOKE_DIR");
        if (string.IsNullOrWhiteSpace(_evidence)) return;
        _evidence = Path.GetFullPath(_evidence);
        Directory.CreateDirectory(_evidence);
        CaptureCryptoProvider("enabled");
        FacilityScenario.Initialize(_evidence);
        FacilityEventContractProbe.Initialize(_evidence);
        RecipientScenario.Initialize(_evidence);
        CustomItems.Register(new CustomItemDefinition
        {
            Key = ItemKey,
            BaseTemplate = ItemType.Medkit,
            DisplayName = "Smoke medical kit",
            Description = "Test-only custom usable item.",
            IconAddress = AssetAddress.Parse("mods/fixture/textures/checker"),
        });
        AnomalyMessageRegistry.Register(SmokeRequest.Name, () => new SmokeRequest());
        AnomalyMessageRegistry.Register(SmokeResponse.Name, () => new SmokeResponse());
        StaticUnityMethods.OnUpdate += RegisterBundleWord;
        ClientEvents.Connected += OnConnected;
        ClientEvents.ContentReady += OnContentReady;
        ClientEvents.CapabilitiesChanged += OnCapabilitiesChanged;
        ClientEvents.MessageReceived += OnMessage;
        ClientEvents.MotionApplied += OnMotionApplied;
        PlayerEvents.UsedItem += OnUsedItem;
        PlayerEvents.Left += OnLeft;
        ServerEvents.MapGenerated += OnMapGenerated;
        ServerEvents.MapGenerating += FacilityScenario.Configure;
        _enabled = true;
        Record("enabled", "{\"fixture\":true}");
    }

    public override void Disable()
    {
        if (!_enabled) return;
        StaticUnityMethods.OnUpdate -= RegisterBundleWord;
        ClientEvents.Connected -= OnConnected;
        ClientEvents.ContentReady -= OnContentReady;
        ClientEvents.CapabilitiesChanged -= OnCapabilitiesChanged;
        ClientEvents.MessageReceived -= OnMessage;
        ClientEvents.MotionApplied -= OnMotionApplied;
        PlayerEvents.UsedItem -= OnUsedItem;
        PlayerEvents.Left -= OnLeft;
        ServerEvents.MapGenerated -= OnMapGenerated;
        ServerEvents.MapGenerating -= FacilityScenario.Configure;
        StaticUnityMethods.OnUpdate -= CaptureFacilityOnNextUpdate;
        FacilityScenario.Shutdown();
        FacilityEventContractProbe.Shutdown();
        foreach (Player player in Player.List.ToArray()) PlayerMotion.Reset(player, MotionOwner);
        _enabled = false;
        Record("disabled", "{}");
    }

    private void RegisterBundleWord()
    {
        if (!AssetRegistry.IsReady) return;
        StaticUnityMethods.OnUpdate -= RegisterBundleWord;
        try { Announcer.AddWord(WordName, AssetAddress.Parse("mods/fixture/audio/tone")); }
        catch (Exception exception) { Record("word-registration-failed", "{\"error\":" + Quote(exception.Message) + "}"); }
    }
    private void OnConnected(ClientEventArgs ev)
    {
        CaptureCryptoProvider("connected");
        _connected[ev.Player.NetworkId] = DateTime.UtcNow;
        bool prematureGrant = CustomItems.TryGiveItem(ev.Player, ItemKey, out _);
        Record("connected", Status(ev.Player, "\"prematureGrant\":" + Boolean(prematureGrant)));
        if (prematureGrant) File.WriteAllText(Path.Combine(_evidence, "failure.txt"), "Custom item was granted before catalog activation.");
    }

    private void OnContentReady(ClientEventArgs ev)
    {
        _contentReadyCount++;
        Record("content-ready", Status(ev.Player));
    }

    private void OnCapabilitiesChanged(ClientEventArgs ev) => Record("capabilities", Status(ev.Player));
    private void OnMotionApplied(PlayerMotionAppliedEventArgs ev)
    {
        if (ev.OwnerId != MotionOwner) return;
        _motionCount++;
        Record("motion", Status(ev.Player));
    }
    private void OnUsedItem(PlayerUsedItemEventArgs ev)
    {
        if (CustomItems.TryGetItemKey(ev.UsableItem.Type, out string key) && key == ItemKey)
        {
            _usedCount++;
            Record("native-used-item", Status(ev.Player));
        }
    }
    private void OnLeft(PlayerLeftEventArgs ev)
    {
        uint id = ev.Player.NetworkId;
        _connected.Remove(id);
        _granted.Remove(id);
        _motionSequences.Remove(id);
        ClearTextures(ev.Player);
        PlayerMotion.Reset(ev.Player, MotionOwner);
        Record("left", "{\"netId\":" + id + "}");
    }

    private void OnMessage(ClientMessageReceivedEventArgs ev)
    {
        if (ev.Message is not SmokeRequest request) return;
        try
        {
            string raw = request.Command ?? "";
            string[] command = raw.IndexOf(':') >= 0 ? raw.Split(':') : raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string operation = command.Length == 0 ? "status" : command[0];
            string details;
            bool success = true;
            bool restartFacility = false;
            switch (operation)
            {
                case "status":
                case "report":
                    CaptureFacilityDiagnostics();
                    details = Status(ev.Player);
                    break;
                case "prepare":
                    ev.Player.Role = RoleTypeId.ClassD;
                    ev.Player.Health = 50f;
                    details = Status(ev.Player);
                    break;
                case "recipients": details = RecipientScenario.Handle(ev.Player, command); break;
                case "view-room":
                    _motionSequences.TryGetValue(ev.Player.NetworkId, out uint viewSequence);
                    details = FacilityView.Apply(ev.Player, command, MotionOwner, viewSequence + 1, out success);
                    if (success) _motionSequences[ev.Player.NetworkId] = viewSequence + 1;
                    break;
                case "exercise": details = Exercise(ev.Player, out success); break;
                case "look":
                    if (command.Length != 2) throw new ArgumentException("look:<degrees>");
                    float degrees = float.Parse(command[1], CultureInfo.InvariantCulture);
                    _motionSequences.TryGetValue(ev.Player.NetworkId, out uint lookSequence);
                    var look = new MotionDiscontinuity(ev.Player.Position, Vector3.zero,
                        Quaternion.Euler(0, degrees, 0) * Vector3.forward, lookSequence + 1, true);
                    success = PlayerMotion.TryApply(MotionOwner, ev.Player, look, out MotionFailure lookFailure);
                    if (success) _motionSequences[ev.Player.NetworkId] = lookSequence + 1;
                    details = Status(ev.Player, "\"motionFailure\":" + Quote(lookFailure.ToString()));
                    break;
                case "texture":
                    if (command.Length != 4) throw new ArgumentException("texture <netId> <base64-path-or-dash> <material-index>");
                    uint target = uint.Parse(command[1], CultureInfo.InvariantCulture);
                    string path = command[2] == "-" || command[2] == "" ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(command[2]));
                    int index = int.Parse(command[3], CultureInfo.InvariantCulture);
                    if (CustomItems.GetPlayerReadiness(ev.Player) != ContentReadiness.Activated)
                        throw new InvalidOperationException("Content has not activated.");
                    AssetOverrides.SetTexture(target, path, index, AssetAddress.Parse("mods/fixture/textures/checker"));
                    if (!_textureTargets.TryGetValue(ev.Player.NetworkId, out var targets))
                        _textureTargets.Add(ev.Player.NetworkId, targets = new HashSet<uint>());
                    targets.Add(target);
                    details = Status(ev.Player, "\"targetNetId\":" + target + ",\"componentPath\":" + Quote(path) + ",\"materialIndex\":" + index);
                    break;
                case "clear":
                    ClearTextures(ev.Player);
                    PlayerMotion.Reset(ev.Player, MotionOwner);
                    details = Status(ev.Player);
                    break;
                case "facility":
                case "facility-restart":
                    if (command.Length != 2) throw new ArgumentException("facility:<pristine|noop|lcz|hcz|entrance|atlas|planned-noop|planned-move|planned-remove|planned-checkpoint|planned-recipe-noop>");
                    FacilityScenario.Select(command[1]);
                    restartFacility = operation == "facility-restart";
                    details = "{\"nextFacilityScenario\":" + Quote(command[1]) + "}";
                    break;
                default: throw new ArgumentException("Unknown fixture command: " + operation);
            }
            Record(operation, details);
            ClientMessaging.Send(ev.Player, new SmokeResponse { Token = request.Token, Success = success, Details = details });
            if (restartFacility) Round.RestartSilently();
        }
        catch (Exception exception)
        {
            string details = "{\"error\":" + Quote(exception.ToString()) + "}";
            Record("request-failure", details);
            ClientMessaging.TrySend(ev.Player, new SmokeResponse { Token = request.Token, Success = false, Details = details });
        }
    }

    private string Exercise(Player player, out bool success)
    {
        if (CustomItems.GetPlayerReadiness(player) != ContentReadiness.Activated)
        {
            success = false;
            return Status(player, "\"granted\":false,\"error\":\"Content has not activated\"");
        }
        _motionSequences.TryGetValue(player.NetworkId, out uint sequence);
        var motion = new MotionDiscontinuity(player.Position + new Vector3(0f, 0.05f, 0f), Vector3.zero,
            player.Rotation * Vector3.forward, sequence + 1, true);
        bool moved = PlayerMotion.TryApply(MotionOwner, player, motion, out MotionFailure failure);
        if (moved) _motionSequences[player.NetworkId] = sequence + 1;
        bool granted = _granted.TryGetValue(player.NetworkId, out Item item);
        if (!granted && moved)
        {
            granted = CustomItems.TryGiveItem(player, ItemKey, out item);
            if (granted) _granted[player.NetworkId] = item;
        }
        if (granted) player.CurrentItem = item;
        success = moved && granted && ClientCapabilities.Supports(player, Requirement);
        return Status(player, "\"granted\":" + Boolean(granted) + ",\"itemSerial\":" + (item?.Serial ?? 0)
            + ",\"moved\":" + Boolean(moved) + ",\"motionFailure\":" + Quote(failure.ToString())
            + ",\"position\":{\"x\":" + Number(motion.Position.x) + ",\"y\":" + Number(motion.Position.y) + ",\"z\":" + Number(motion.Position.z) + "}");
    }

    private string Status(Player player, string extra = null)
    {
        CustomItems.TryGetItemId(ItemKey, out ItemType itemType);
        AssetAddress textureAddress = AssetAddress.Parse("mods/fixture/textures/checker");
        _connected.TryGetValue(player.NetworkId, out DateTime since);
        return "{\"netId\":" + player.NetworkId + ",\"readiness\":" + Quote(CustomItems.GetPlayerReadiness(player).ToString())
            + ",\"capability\":" + Boolean(ClientCapabilities.Supports(player, Requirement))
            + ",\"itemId\":" + (int)itemType + ",\"textureAddress\":" + Quote(AssetRegistry.Contains(textureAddress) ? textureAddress.Value : "")
            + ",\"wordName\":" + Quote(WordName) + ",\"contentReadyCount\":" + _contentReadyCount
            + ",\"usedCount\":" + _usedCount + ",\"motionCount\":" + _motionCount
            + ",\"connectedSeconds\":" + Number(since == default ? 0 : (DateTime.UtcNow - since).TotalSeconds)
            + ",\"roomDigest\":" + Quote(RoomDigest()) + (extra == null ? "" : "," + extra) + "}";
    }

    private void ClearTextures(Player player)
    {
        if (!_textureTargets.TryGetValue(player.NetworkId, out var targets)) return;
        foreach (uint target in targets) AssetOverrides.ClearAll(target);
        _textureTargets.Remove(player.NetworkId);
    }

    private void OnMapGenerated(MapGeneratedEventArgs _)
    {
        string[] rows = RoomRows();
        File.WriteAllLines(Path.Combine(_evidence, "rooms.txt"), rows);
        Record("map-generated", "{\"roomDigest\":" + Quote(Digest(rows)) + ",\"roomCount\":" + rows.Length + "}");
        CaptureFacilityDiagnostics();
        StaticUnityMethods.OnUpdate -= CaptureFacilityOnNextUpdate;
        StaticUnityMethods.OnUpdate += CaptureFacilityOnNextUpdate;
    }

    private void CaptureFacilityOnNextUpdate()
    {
        StaticUnityMethods.OnUpdate -= CaptureFacilityOnNextUpdate;
        if (_enabled) CaptureFacilityDiagnostics();
    }

    private void CaptureFacilityDiagnostics()
    {
        Type diagnostics = typeof(Anomaly.Server.Plugin).Assembly.GetType("Anomaly.Server.Internal.Map.FacilityGenerationDiagnostics");
        var capture = diagnostics?.GetMethod("Capture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (capture?.Invoke(null, null) is string json)
        {
            File.WriteAllText(Path.Combine(_evidence, "facility.json"), json);
            FacilityScenario.Capture(json);
        }
    }

    private static string RoomDigest() => Digest(RoomRows());
    private static string[] RoomRows() => Room.List.Select(room => string.Join("|", new[]
    {
        ((int)room.Zone).ToString(CultureInfo.InvariantCulture), ((int)room.Name).ToString(CultureInfo.InvariantCulture),
        ((int)room.Shape).ToString(CultureInfo.InvariantCulture), Quantize(room.Position.x), Quantize(room.Position.y),
        Quantize(room.Position.z), Quantize(room.Rotation.eulerAngles.y),
    })).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    private static string Quantize(float value) => Math.Round(value * 100d, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
    private static string Digest(string[] rows)
    {
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
    }
    private void Record(string stage, string details)
    {
        string line = "{\"utc\":" + Quote(DateTime.UtcNow.ToString("O")) + ",\"stage\":" + Quote(stage) + ",\"details\":" + details + "}";
        File.AppendAllText(Path.Combine(_evidence, "events.jsonl"), line + Environment.NewLine);
        Logger.Info("Server API smoke: " + stage);
    }
    private void CaptureCryptoProvider(string phase)
    {
        var providers = new List<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(value => value.GetName().Name == "BouncyCastle.Cryptography"))
        {
            string location;
            try { location = assembly.Location; }
            catch (NotSupportedException) { location = null; }

            string sha256 = null;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
            {
                using var stream = File.OpenRead(location);
                using var hash = SHA256.Create();
                sha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
            providers.Add("{\"assembly\":" + Quote(assembly.FullName) +
                          ",\"location\":" + Quote(location) +
                          ",\"sha256\":" + Quote(sha256) + "}");
        }
        File.WriteAllText(Path.Combine(_evidence, "bouncycastle-" + phase + ".json"),
            "[" + string.Join(",", providers) + "]");
    }
    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        foreach (char character in value ?? "")
        {
            if (character == '"' || character == '\\') text.Append('\\').Append(character);
            else if (character < 32) text.Append("\\u").Append(((int)character).ToString("x4"));
            else text.Append(character);
        }
        return text.Append('"').ToString();
    }
}

public sealed class Config { }
