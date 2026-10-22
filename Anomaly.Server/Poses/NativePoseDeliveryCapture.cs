using System;
using System.Collections.Generic;
using Mirror;

namespace Anomaly.Server.Poses;

/// <summary>
/// Captures roles during serialization, but commits only after the exact packed byte segment
/// enters Mirror's reliable batch. A void Send that drops an oversize message never commits.
/// </summary>
internal static class NativePoseDeliveryCapture
{
    internal sealed class Record
    {
        internal uint Target;
        internal uint Recipient;
        internal sbyte Role;
        internal int Life;
    }
    internal sealed class Pending
    {
        internal ArraySegment<byte> Segment;
        internal Record[] Records;
    }
    [ThreadStatic] private static NetworkWriter _packWriter;
    [ThreadStatic] private static List<Record> _packRecords;
    [ThreadStatic] private static Pending _pending;

    internal static void BeginPack(NetworkWriter writer)
    {
        _packWriter = writer;
        _packRecords = new List<Record>();
        _pending = null;
    }
    internal static void RoleWritten(NetworkWriter writer, Record record)
    {
        if (ReferenceEquals(writer, _packWriter)) { _packRecords.Add(record); return; }
        _pending = new Pending { Segment = writer.ToArraySegment(), Records = new[] { record } };
    }
    internal static void EndPack(NetworkWriter writer)
    {
        if (ReferenceEquals(writer, _packWriter))
            _pending = new Pending { Segment = writer.ToArraySegment(), Records = _packRecords.ToArray() };
        _packWriter = null;
        _packRecords = null;
    }
    internal static void AbortPack() { _packWriter = null; _packRecords = null; _pending = null; }

    internal static Pending BeforeEnqueue(ArraySegment<byte> segment, int channel)
    {
        Pending value = _pending;
        if (channel != 0 || value == null || !ReferenceEquals(value.Segment.Array, segment.Array) ||
            value.Segment.Offset != segment.Offset || value.Segment.Count != segment.Count || segment.Count < 2)
            return null;
        ushort id = BitConverter.ToUInt16(segment.Array, segment.Offset);
        if (id != NetworkMessageId<PlayerRoles.RoleSyncInfo>.Id && id != NetworkMessageId<PlayerRoles.RoleSyncInfoPack>.Id)
            return null;
        _pending = null;
        return value;
    }
    internal static void AfterEnqueue(Pending pending, NetworkConnection connection)
    {
        if (pending == null) return;
        PlayerPoseRuntime.BeginRoleDelivery();
        try
        {
            foreach (Record record in pending.Records)
            {
                if (!ReferenceHub.TryGetHubNetID(record.Recipient, out ReferenceHub hub) || hub.connectionToClient != connection) continue;
                PlayerPoseRuntime.RoleDelivered(record.Target, record.Recipient, record.Role, record.Life);
            }
        }
        finally { PlayerPoseRuntime.EndRoleDelivery(); }
    }
}
