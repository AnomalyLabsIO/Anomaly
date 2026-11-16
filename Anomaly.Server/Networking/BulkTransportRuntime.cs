using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Anomaly.Shared.Networking;
using HarmonyLib;
using LiteNetLib;
using Mirror;
using Mirror.LiteNetLib4Mirror;

namespace Anomaly.Server.Networking;

/// <summary>Owns extra physical reliable windows before peers allocate their native lane state.</summary>
internal static class BulkTransportRuntime
{
    private const int MaximumQueuedPacketsPerLane = 64;
    private static LiteNetLib4MirrorTransport _transport;
    private static DeliveryMethod[] _mapping;
    private static NetManager _host;
    private static ulong _ownedMask;
    private static bool _enabled;
    // Mirror flushes later through Transport.active. Retain the route that owned
    // each connection's queued bulk lanes, without retaining retired connections.
    private static readonly ConditionalWeakTable<NetworkConnectionToClient, QueuedBulkRoute> QueuedRoutes =
        new ConditionalWeakTable<NetworkConnectionToClient, QueuedBulkRoute>();

    internal static MethodBase SetOptionsTarget => AccessTools.Method(typeof(LiteNetLib4MirrorCore), "SetOptions");
    internal static MethodBase BatchThresholdTarget => AccessTools.Method(typeof(LiteNetLib4MirrorTransport),
        nameof(Transport.GetBatchThreshold), new[] { typeof(int) });
    internal static MethodBase SendToTransportTarget => AccessTools.Method(typeof(NetworkConnectionToClient),
        "SendToTransport", new[] { typeof(ArraySegment<byte>), typeof(int) });

    internal static void Initialize()
    {
        _enabled = true;
        var transport = LiteNetLib4MirrorTransport.Singleton;
        var host = LiteNetLib4MirrorCore.Host;
        if (transport == null || !IsActiveTransport(transport)) return;
        if (host != null && host.FirstPeer != null && !IsPrepared(transport, host))
            throw new InvalidOperationException("Anomaly bulk channels must be prepared before any peer exists. Restart the server.");
        PrepareMapping(transport);
        if (host != null)
        {
            host.ChannelsCount = checked((byte)transport.channels.Length);
            _host = host;
        }
    }

    internal static void Disable()
    {
        _enabled = false;
        _host = null;
        // Keep the unchanged table's ownership across teardown. Never shrink a live host
        // or append another pool to the same already-expanded scene transport.
    }

    internal static bool TryGetProfile(out byte version, out ulong mask)
    {
        version = BulkTransferProtocol.ProfileVersion;
        mask = 0;
        if (!_enabled || !IsPrepared(LiteNetLib4MirrorTransport.Singleton, LiteNetLib4MirrorCore.Host)) return false;
        mask = _ownedMask;
        return mask != 0;
    }

    internal static bool IsBulkPhysicalChannel(int channel)
    {
        var transport = LiteNetLib4MirrorTransport.Singleton;
        return (uint)channel < BulkTransferProtocol.MaxPhysicalChannels &&
            IsPrepared(transport, LiteNetLib4MirrorCore.Host) && (_ownedMask & (1UL << channel)) != 0;
    }

    internal static bool TryGetRoute(NetworkConnectionToClient connection, ulong selectedMask, int domain,
        out int physicalChannel, out bool backpressured)
    {
        physicalChannel = -1;
        backpressured = false;
        if (!TryGetProfile(out _, out ulong owned) || !BulkTransferProtocol.IsValidMask(selectedMask, allowEmpty: false) ||
            (selectedMask & owned) != selectedMask || (uint)domain >= BulkTransferProtocol.DomainCount(selectedMask) ||
            connection == null || LiteNetLib4MirrorServer.Peers == null ||
            !LiteNetLib4MirrorServer.Peers.TryGetValue(connection.connectionId, out var peer) ||
            peer == null || peer.ConnectionState != ConnectionState.Connected)
            return false;
        physicalChannel = BulkTransferProtocol.GetPhysicalChannel(selectedMask, domain);
        int queued = peer.GetPacketsCountInReliableQueue((byte)physicalChannel, false);
        if (queued < 0) return false;
        if (!QueuedRoutes.TryGetValue(connection, out var owner))
        {
            owner = new QueuedBulkRoute(_transport, _host, _mapping);
            QueuedRoutes.Add(connection, owner);
        }
        if (!RouteIntact(owner)) return false;
        owner.PhysicalMask |= 1UL << physicalChannel;
        backpressured = queued >= MaximumQueuedPacketsPerLane;
        return true;
    }

    internal static bool TryGetLinkCapacity(NetworkConnectionToClient connection, ulong selectedMask,
        out int effectiveMtu, out int roundTripTimeMilliseconds)
    {
        effectiveMtu = roundTripTimeMilliseconds = 0;
        if (!TryGetProfile(out _, out ulong owned) || !BulkTransferProtocol.IsValidMask(selectedMask, allowEmpty: false) ||
            (selectedMask & owned) != selectedMask || connection == null || LiteNetLib4MirrorServer.Peers == null ||
            !LiteNetLib4MirrorServer.Peers.TryGetValue(connection.connectionId, out var peer) ||
            peer == null || peer.ConnectionState != ConnectionState.Connected)
            return false;
        effectiveMtu = peer.Mtu;
        roundTripTimeMilliseconds = peer.RoundTripTime;
        return effectiveMtu > 10 && roundTripTimeMilliseconds >= 0;
    }

    private static bool IsPrepared(LiteNetLib4MirrorTransport transport, NetManager host) =>
        _enabled && IsActiveTransport(transport) && host != null && ReferenceEquals(host, _host) && ReferenceEquals(transport, _transport) &&
        host.ChannelsCount == transport.channels.Length && MappingIntact(transport);

    private static bool IsActiveTransport(LiteNetLib4MirrorTransport transport) =>
        transport != null && ReferenceEquals(Transport.active, transport);

    private static bool RouteIntact(QueuedBulkRoute owner) =>
        ReferenceEquals(LiteNetLib4MirrorTransport.Singleton, owner.Transport) &&
        ReferenceEquals(LiteNetLib4MirrorCore.Host, owner.Host) &&
        IsPrepared(owner.Transport, owner.Host) && ReferenceEquals(owner.Mapping, _mapping);

    private static bool CanFlush(NetworkConnectionToClient connection, int physicalChannel)
    {
        if (connection == null || (uint)physicalChannel >= BulkTransferProtocol.MaxPhysicalChannels ||
            !QueuedRoutes.TryGetValue(connection, out var owner) ||
            (owner.PhysicalMask & (1UL << physicalChannel)) == 0)
            return true; // Other mods' lanes and connections are outside this guard.
        if (RouteIntact(owner)) return true;
        ConnectionBootstrap.Reject(connection,
            "The active transport changed before queued authenticated content could be delivered.");
        return false;
    }

    private static bool MappingIntact(LiteNetLib4MirrorTransport transport)
    {
        var current = transport?.channels;
        if (current == null || _mapping == null || current.Length != _mapping.Length) return false;
        for (int index = 0; index < current.Length; index++)
            if (current[index] != _mapping[index]) return false;
        return true;
    }

    private static void PrepareMapping(LiteNetLib4MirrorTransport transport)
    {
        if (ReferenceEquals(transport, _transport) && MappingIntact(transport)) return;
        var existing = transport.channels;
        if (existing == null || existing.Length < 5 || existing.Length >= BulkTransferProtocol.MaxPhysicalChannels ||
            existing[0] != DeliveryMethod.ReliableOrdered || existing[1] != DeliveryMethod.Unreliable ||
            existing[2] != DeliveryMethod.Sequenced || existing[3] != DeliveryMethod.ReliableSequenced ||
            existing[4] != DeliveryMethod.ReliableUnordered)
            throw new InvalidOperationException("The configured transport mapping cannot provide Anomaly bulk channels safely.");
        foreach (var delivery in existing)
            if (!Enum.IsDefined(typeof(DeliveryMethod), delivery))
                throw new InvalidOperationException("The transport contains an unsupported delivery method.");
        int first = Math.Max(existing.Length, BulkTransferProtocol.FirstPhysicalChannel);
        int count = Math.Min(BulkTransferProtocol.MaxDomains, BulkTransferProtocol.MaxPhysicalChannels - first);
        if (count <= 0) throw new InvalidOperationException("No physical channels remain for Anomaly bulk transfers.");
        var expanded = new DeliveryMethod[first + count];
        Array.Copy(existing, expanded, existing.Length);
        for (int index = existing.Length; index < first; index++) expanded[index] = DeliveryMethod.ReliableOrdered;
        ulong mask = 0;
        for (int index = first; index < expanded.Length; index++)
        {
            expanded[index] = DeliveryMethod.ReliableUnordered;
            mask |= 1UL << index;
        }
        transport.channels = expanded;
        _transport = transport;
        _mapping = (DeliveryMethod[])expanded.Clone();
        _ownedMask = mask;
    }

    [HarmonyPatch]
    private static class OptionsPatch
    {
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() => SetOptionsTarget ?? throw new MissingMethodException("Transport SetOptions was not found.");

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(bool __0)
        {
            if (!_enabled || !__0) return;
            var host = LiteNetLib4MirrorCore.Host;
            var transport = LiteNetLib4MirrorTransport.Singleton;
            if (!IsActiveTransport(transport)) return;
            if (transport == null || (host?.FirstPeer != null && !IsPrepared(transport, host)))
                throw new InvalidOperationException("Bulk channel capacity cannot change after native peers are constructed.");
            PrepareMapping(transport);
        }

        [HarmonyPostfix]
        private static void Postfix(bool __0)
        {
            if (!_enabled || !__0) return;
            var transport = LiteNetLib4MirrorTransport.Singleton;
            var host = LiteNetLib4MirrorCore.Host;
            if (!IsActiveTransport(transport)) { _host = null; return; }
            if (host == null || !MappingIntact(transport) || host.ChannelsCount != transport.channels.Length)
                throw new InvalidOperationException("Native bulk channel capacity did not match the configured mapping.");
            _host = host;
        }
    }

    [HarmonyPatch]
    private static class BatchThresholdPatch
    {
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() => BatchThresholdTarget ?? throw new MissingMethodException("Transport batch threshold was not found.");

        [HarmonyPrefix]
        private static bool Prefix(Transport __instance, int __0, ref int __result)
        {
            if (!ReferenceEquals(__instance, _transport) || !IsBulkPhysicalChannel(__0)) return true;
            __result = BulkTransferProtocol.BatchThresholdBytes;
            return false;
        }
    }

    private sealed class QueuedBulkRoute
    {
        internal readonly LiteNetLib4MirrorTransport Transport;
        internal readonly NetManager Host;
        internal readonly DeliveryMethod[] Mapping;
        internal ulong PhysicalMask;

        internal QueuedBulkRoute(LiteNetLib4MirrorTransport transport, NetManager host, DeliveryMethod[] mapping)
        { Transport = transport; Host = host; Mapping = mapping; }
    }

    [HarmonyPatch]
    private static class BulkFlushPatch
    {
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() => SendToTransportTarget ??
            throw new MissingMethodException("Mirror connection transport delivery was not found.");

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(NetworkConnectionToClient __instance, int __1) => CanFlush(__instance, __1);
    }
}
