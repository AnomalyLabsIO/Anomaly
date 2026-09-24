using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Anomaly.Server.Api.Features;
using LabApi.Features.Wrappers;
using MapGeneration;
using UnityEngine;

namespace Anomaly.ServerApiSmoke;

internal static class FacilityView
{
    internal static string Apply(Player player, string[] command, string owner, uint sequence, out bool success)
    {
        if (command.Length != 5) throw new ArgumentException("view-room:<zone>:<x>:<y>:<yaw>");
        var zone = (FacilityZone)int.Parse(command[1], CultureInfo.InvariantCulture);
        int x = int.Parse(command[2], CultureInfo.InvariantCulture);
        int y = int.Parse(command[3], CultureInfo.InvariantCulture);
        float yaw = float.Parse(command[4], CultureInfo.InvariantCulture);
        if (float.IsNaN(yaw) || float.IsInfinity(yaw)) throw new ArgumentOutOfRangeException(nameof(yaw));
        Room room = null;
        foreach (var generator in UnityEngine.Object.FindObjectsByType<AtlasZoneGenerator>(FindObjectsSortMode.None))
        {
            if (generator.TargetZone != zone) continue;
            foreach (var spawned in generator.Spawned)
                if (spawned.Interpretation.Coords.x == x && spawned.Interpretation.Coords.y == y)
                    room = Room.Get(spawned.Instance.Room);
        }
        if (room == null) throw new InvalidOperationException("No generated room exists at the requested atlas coordinate.");
        if (!TryFindStandingPosition(room, player, out Vector3 position))
            throw new InvalidOperationException("No clear standing position was found in the requested room.");

        var motion = new MotionDiscontinuity(position, Vector3.zero, Quaternion.Euler(0, yaw, 0) * Vector3.forward, sequence, true);
        success = PlayerMotion.TryApply(owner, player, motion, out MotionFailure failure);
        return JsonSerializer.Serialize(new
        {
            zone = (int)zone, x, y, yaw,
            roomName = room.Name.ToString(),
            position = new { x = position.x, y = position.y, z = position.z },
            roomPosition = new { x = room.Position.x, y = room.Position.y, z = room.Position.z },
            motionFailure = failure.ToString(),
        });
    }

    private static bool TryFindStandingPosition(Room room, Player player, out Vector3 position)
    {
        foreach (Vector3 offset in new[]
        {
            Vector3.zero, new Vector3(2, 0, 0), new Vector3(-2, 0, 0),
            new Vector3(0, 0, 2), new Vector3(0, 0, -2), new Vector3(3, 0, 3), new Vector3(-3, 0, -3),
        })
        {
            Vector3 origin = room.Transform.TransformPoint(offset) + Vector3.up * 2.5f;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore).OrderBy(hit => hit.distance))
            {
                if (hit.normal.y < 0.7f || BelongsToPlayer(hit.collider, player)) continue;
                Vector3 foot = hit.point + Vector3.up * 0.08f;
                bool blocked = Physics.OverlapCapsule(foot + Vector3.up * 0.4f, foot + Vector3.up * 1.45f,
                    0.3f, ~0, QueryTriggerInteraction.Ignore).Any(collider => !BelongsToPlayer(collider, player));
                if (blocked) continue;
                position = foot + Vector3.up;
                return true;
            }
        }
        position = default;
        return false;
    }

    private static bool BelongsToPlayer(Collider collider, Player player) =>
        collider != null && player.GameObject != null && collider.transform.IsChildOf(player.GameObject.transform);
}
