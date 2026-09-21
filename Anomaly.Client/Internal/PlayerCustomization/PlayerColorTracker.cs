using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Rendering;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Il2CppMirror;
using Il2CppPlayerRoles;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Internal.PlayerCustomization;

/// <summary>
/// Owns the local player's chosen Class D jumpsuit color, the cache of remote
/// players' colors received from the server, and the wiring that applies the
/// tint to a Class D character model whenever role changes or color updates
/// arrive. Stateless renderer manipulation is delegated to <see cref="MaterialTint"/>.
/// </summary>
internal static class PlayerColorTracker
{
    private const int MaximumRemotePlayerColors = 256;
    private const string ConfigModId = "anomaly";
    private const string FavoriteColorEntryId = "class_d_favorite_color";
    private const string DefaultColorHex = "#FFFFFF";
    private const string JumpsuitGameObjectPrefix = "jumpsuit_LOD";
    private const string NeutralBaseMapResource =
        "Anomaly.Client.Resources.PlayerCustomization.ClassD_jumpsuit_BaseMap_neutral.png";

    private static Color _localColor = Color.white;
    private static readonly Dictionary<uint, Color> _byNetId = new();
    private static readonly RemoteStateCapacityGuard<uint, uint> Capacity =
        new(MaximumRemotePlayerColors, 1);
    private static Texture2D _neutralBaseMap;
    private static MelonPreferences_Category _preferences;
    private static MelonPreferences_Entry<string> _favoriteColorEntry;
    private static readonly TransactionalInitializer Initializer = new();
    private static bool _resourcesInitialized;

    internal static void Init()
    {
        if (!_resourcesInitialized)
        {
            try
            {
                _neutralBaseMap = LoadEmbeddedNeutralBaseMap();
                if (_neutralBaseMap == null)
                {
                    MelonLogger.Warning(
                        $"PlayerColorTracker: neutral base map '{NeutralBaseMapResource}' not embedded; " +
                        "falling back to _BaseColor multiply only (warm-palette tints look correct, " +
                        "cool-palette tints look dark/black against the orange jumpsuit).");
                }

                EnsurePreferences();
                _localColor = LoadLocalColor();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"PlayerColorTracker.Init (resources/config): {ex}");
            }
            _resourcesInitialized = true;
        }

        Initializer.Initialize(
            new InitializationStep(
                () => AnomalyMessaging.MessageReceived += HandleMessageReceived,
                () => AnomalyMessaging.MessageReceived -= HandleMessageReceived),
            new InitializationStep(
                () => RoleEvents.Changed += HandleRoleChanged,
                () => RoleEvents.Changed -= HandleRoleChanged),
            new InitializationStep(
                () => PlayerEvents.Removed += HandlePlayerRemoved,
                () => PlayerEvents.Removed -= HandlePlayerRemoved),
            new InitializationStep(
                () => ClientEvents.Ready += HandleClientReady,
                () => ClientEvents.Ready -= HandleClientReady),
            new InitializationStep(
                () => ClientEvents.Disconnected += HandleClientDisconnected,
                () => ClientEvents.Disconnected -= HandleClientDisconnected));
    }

    /// <summary>
    /// Public entry point for changing the local player's color mid-game (e.g.
    /// from a future config-watcher, slash command, or settings UI). Updates
    /// in-memory state, persists to MelonPreferences, re-applies to
    /// the local model if currently Class D, and notifies the server so other
    /// clients can rebroadcast.
    /// </summary>
    internal static void SetLocalColor(Color color)
    {
        _localColor = color;

        try
        {
            EnsurePreferences();
            _favoriteColorEntry.Value = ColorToHex(color);
            _preferences.SaveToFile(printmsg: false);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"PlayerColorTracker.SetLocalColor (config save): {ex}");
        }

        var local = LocalPlayer.Instance;
        if (local != null && local.Role != null && local.Role.Type == RoleTypeId.ClassD)
            ApplyOrClear(local, color);

        SendLocalColor();
    }

    private static void SendLocalColor()
    {
        try
        {
            var (r, g, b) = ColorToBytes(_localColor);
            AnomalyMessaging.Send(new SetFavoriteColorMessage { R = r, G = g, B = b });
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"PlayerColorTracker.SendLocalColor: {ex}");
        }
    }

    private static void HandleMessageReceived(IAnomalyMessage msg)
    {
        if (msg is not PlayerColorChangedMessage pcm)
            return;

        try
        {
            if (pcm.NetId == 0 || !Capacity.TryAdd(pcm.NetId, pcm.NetId))
            {
                RejectRemoteState(
                    pcm.NetId == 0
                        ? "received a player color for reserved netId zero"
                        : $"player-color capacity exceeded ({MaximumRemotePlayerColors})");
                return;
            }

            var color = BytesToColor(pcm.R, pcm.G, pcm.B);
            _byNetId[pcm.NetId] = color;

            // If a Player exists for this NetId and they're currently Class D,
            // re-paint immediately so mid-game color changes show without
            // needing a respawn.
            var target = FindPlayerByNetId(pcm.NetId);
            if (target != null && target.Role != null && target.Role.Type == RoleTypeId.ClassD)
                ApplyOrClear(target, color);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"PlayerColorTracker.HandleMessageReceived: {ex}");
        }
    }

    private static void HandleRoleChanged(PlayerRoleChangedEventArgs args)
    {
        try
        {
            // Only act on transitions INTO ClassD. Models leaving the role get
            // pooled and the next ClassD spawn-out of the pool is handled by
            // the next role-change event re-applying (or clearing) the MPB.
            if (args == null || args.Player == null || args.NewRole == null)
                return;
            if (args.NewRole.Type != RoleTypeId.ClassD)
                return;

            var color = ColorFor(args.Player);
            ApplyOrClear(args.Player, color);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"PlayerColorTracker.HandleRoleChanged: {ex}");
        }
    }

    private static void HandlePlayerRemoved(PlayerRemovedEventArgs args)
    {
        try
        {
            if (args?.Player?.ReferenceHub == null)
                return;
            uint netId = args.Player.ReferenceHub.netId;
            _byNetId.Remove(netId);
            Capacity.Remove(netId, netId);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"PlayerColorTracker.HandlePlayerRemoved: {ex}");
        }
    }

    private static void HandleClientReady(ClientReadyEventArgs _)
    {
        SendLocalColor();
    }

    private static void HandleClientDisconnected(ClientDisconnectedEventArgs _)
    {
        _byNetId.Clear();
        Capacity.Clear();
    }

    private static void RejectRemoteState(string reason)
    {
        _byNetId.Clear();
        Capacity.Clear();
        try { AnomalyMessaging.ClearTransportSession(); }
        catch { /* disconnect remains mandatory */ }
        try { NetworkClient.connection?.Disconnect(); }
        catch { /* the transport may already be closing */ }
        try
        {
            FeatureHealth.Report("anomaly", "networking", FeatureHealthStatus.Failed, reason);
            MelonLogger.Error("Authenticated player-color state was rejected: " + reason);
        }
        catch { /* diagnostics must not weaken fail-closed teardown */ }
    }

    private static Color ColorFor(Player player)
    {
        if (player.IsLocal)
            return _localColor;
        if (_byNetId.TryGetValue(player.ReferenceHub.netId, out var c))
            return c;
        return Color.white;
    }

    private static void ApplyOrClear(Player player, Color color)
    {
        var jumpsuits = FindJumpsuitRenderers(player);
        if (jumpsuits == null || jumpsuits.Count == 0)
            return;

        if (IsNoTint(color))
        {
            MaterialTint.Clear(jumpsuits);
            return;
        }

        if (_neutralBaseMap != null)
            MaterialTint.Apply(jumpsuits, color, _neutralBaseMap);
        else
            MaterialTint.Apply(jumpsuits, color);
    }

    private static List<Renderer> FindJumpsuitRenderers(Player player)
    {
        var model = player?.CharacterModel;
        if (model == null)
            return null;

        var renderers = model._renderers;
        if (renderers == null)
            return null;

        var list = new List<Renderer>(4);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null)
                continue;
            var go = r.gameObject;
            if (go == null)
                continue;
            if (go.name != null && go.name.StartsWith(JumpsuitGameObjectPrefix, StringComparison.Ordinal))
                list.Add(r);
        }
        return list;
    }

    private static Player FindPlayerByNetId(uint netId)
    {
        foreach (var p in Player.PlayerList)
        {
            if (p?.ReferenceHub != null && p.ReferenceHub.netId == netId)
                return p;
        }
        return null;
    }

    private static Color LoadLocalColor()
    {
        try
        {
            EnsurePreferences();
            var hex = string.IsNullOrWhiteSpace(_favoriteColorEntry.Value)
                ? DefaultColorHex
                : _favoriteColorEntry.Value;
            if (ColorUtility.TryParseHtmlString(hex, out var parsed))
                return parsed;
            MelonLogger.Warning(
                $"PlayerColorTracker: '{FavoriteColorEntryId}' value '{hex}' is not a valid color; using default {DefaultColorHex}.");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"PlayerColorTracker.LoadLocalColor: {ex}");
        }
        return Color.white;
    }

    private static void EnsurePreferences()
    {
        _preferences ??= MelonPreferences.CreateCategory(ConfigModId, "Anomaly");
        _favoriteColorEntry ??= _preferences.GetEntry<string>(FavoriteColorEntryId)
            ?? _preferences.CreateEntry(
                FavoriteColorEntryId,
                DefaultColorHex,
                "Class D favorite color",
                "Preferred Class D jumpsuit tint as #RRGGBB. #FFFFFF disables tinting.");
    }

    private static Texture2D LoadEmbeddedNeutralBaseMap()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(NeutralBaseMapResource);
        if (stream == null)
            return null;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return PngLoader.TryDecode(ms.ToArray());
    }

    private static (byte R, byte G, byte B) ColorToBytes(Color c)
    {
        Color32 c32 = c;
        return (c32.r, c32.g, c32.b);
    }

    private static Color BytesToColor(byte r, byte g, byte b)
    {
        return new Color32(r, g, b, 255);
    }

    private static string ColorToHex(Color c)
    {
        Color32 c32 = c;
        return $"#{c32.r:X2}{c32.g:X2}{c32.b:X2}";
    }

    private static bool IsNoTint(Color c)
    {
        // Pure white = "no tint" (multiply by 1). Avoid replacing the original
        // _BaseColorMap for users who explicitly chose white or never set one.
        return c.r >= 0.999f && c.g >= 0.999f && c.b >= 0.999f;
    }
}
