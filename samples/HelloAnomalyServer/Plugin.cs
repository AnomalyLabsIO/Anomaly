using System;
using Anomaly.Server.Api.Content;
using Anomaly.Server.Api.Auth;
using Anomaly.Server.Api.Events.Arguments;
using Anomaly.Server.Api.Events.Handlers;
using Anomaly.Server.Api.Networking;
using Anomaly.Shared.Networking;
using HelloAnomaly.Protocol;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Loader.Features.Plugins;
using MapGeneration;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace HelloAnomalyServer;

/// <summary>An ordinary LabAPI consumer with no Anomaly plugin base class.</summary>
public sealed class Plugin : Plugin<Config>
{
    private const string ItemKey = "example.server:medkit";
    private IDisposable _clientModRequirement;
    public override string Name => "Hello Anomaly Server";
    public override string Description => "Minimal server-owned custom item using native LabAPI events.";
    public override string Author => "AnomalyLabs";
    public override Version Version => new Version(1, 0, 0);
    public override Version RequiredApiVersion => new Version(LabApiProperties.CompiledVersion);

    public override void Enable()
    {
        AnomalyMessageRegistry.Register(HelloRequest.Name, () => new HelloRequest());
        AnomalyMessageRegistry.Register(HelloReply.Name, () => new HelloReply());
        CustomItems.Register(new CustomItemDefinition
        {
            Key = ItemKey,
            BaseTemplate = ItemType.Medkit,
            DisplayName = "Example medical kit",
            Description = "A custom presentation with ordinary medkit behavior.",
        });
        ClientEvents.ContentReady += OnContentReady;
        ClientEvents.MessageReceived += OnMessageReceived;
        PlayerEvents.UsedItem += OnUsedItem;
        MapEvents.LayoutFinalizing += OnLayoutFinalizing;
        if (Config.RequireClientCompanion)
            _clientModRequirement = ClientModRequirements.Register("example.hello", "0.1.0", "0.2.0");
    }

    public override void Disable()
    {
        ClientEvents.ContentReady -= OnContentReady;
        ClientEvents.MessageReceived -= OnMessageReceived;
        PlayerEvents.UsedItem -= OnUsedItem;
        MapEvents.LayoutFinalizing -= OnLayoutFinalizing;
        _clientModRequirement?.Dispose();
        _clientModRequirement = null;
    }

    private void OnLayoutFinalizing(FacilityLayoutFinalizingEventArgs ev)
    {
        if (!Config.EnableFacilityExample) return;
        Logger.Info($"Inspecting {ev.Rooms.Count} selected rooms for generation {ev.GenerationId}, seed {ev.Seed}.");
        foreach (var room in ev.Rooms)
        {
            if (room.Name != Config.FacilityExampleRoom || room.IsRemoved) continue;
            if (Config.RemoveFacilityExampleRoom)
                room.IsRemoved = true;
            else
                room.Position += new Vector3(Config.FacilityExampleOffsetX, 0f, 0f);
            Logger.Info($"Edited {room.Name} at {room.Zone} {room.Coords}; removed={room.IsRemoved}, position={room.Position}.");
            // A replacement model requires its own synchronized content and gameplay setup.
            // Do not retain the descriptor for later mutation or change live room transforms.
            return;
        }
        Logger.Warn($"No included {Config.FacilityExampleRoom} room was selected for this generation.");
    }

    private static void OnMessageReceived(ClientMessageReceivedEventArgs ev)
    {
        if (ev.Message is not HelloRequest request) return;
        // Ordinary authenticated messaging works even when content was declined.
        bool activated = CustomItems.GetPlayerReadiness(ev.Player) == ContentReadiness.Activated;
        if (!ClientMessaging.TrySend(ev.Player, new HelloReply { Token = request.Token, ContentActivated = activated }))
            Logger.Warn("Hello reply could not be delivered; the player may have disconnected.");
    }

    private void OnContentReady(ClientEventArgs ev)
    {
        if (Config.GiveOnContentReady && CustomItems.TryGiveItem(ev.Player, ItemKey, out var item))
            Logger.Info($"Granted {ItemKey}, serial {item.Serial}, to {ev.Player.Nickname}.");
    }

    private static void OnUsedItem(PlayerUsedItemEventArgs ev)
    {
        if (CustomItems.TryGetItemKey(ev.UsableItem.Type, out string key) && key == ItemKey)
            Logger.Info($"{ev.Player.Nickname} used {key} through LabAPI's normal UsedItem event.");
    }
}

public sealed class Config
{
    public bool RequireClientCompanion { get; set; } = false;
    public bool GiveOnContentReady { get; set; } = true;
    public bool EnableFacilityExample { get; set; } = false;
    public RoomName FacilityExampleRoom { get; set; } = RoomName.LczArmory;
    public bool RemoveFacilityExampleRoom { get; set; } = false;
    public float FacilityExampleOffsetX { get; set; } = 15f;
}
