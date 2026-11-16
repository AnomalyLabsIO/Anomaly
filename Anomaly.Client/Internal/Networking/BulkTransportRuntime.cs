using System;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.FileTransfer;
using Anomaly.Shared.Networking;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppLiteNetLib;
using Il2CppMirror;
using Il2CppMirror.LiteNetLib4Mirror;
using MelonLoader;

namespace Anomaly.Client.Internal.Networking;

/// <summary>Owns appended physical transport lanes for this transport instance.</summary>
internal static class BulkTransportRuntime
{
    private static IntPtr _transportPointer;
    private static IntPtr _preparedHostPointer;
    private static IntPtr _pendingHostPointer;
    private static DeliveryMethod[] _mapping;
    private static ulong _ownedMask;

    // Called before the stock SetOptions copies the mapping into a new manager.
    internal static void Prepare()
    {
        var transport = LiteNetLib4MirrorTransport.Singleton;
        var host = LiteNetLib4MirrorCore.Host;
        if (transport == null || transport.channels == null)
            throw new InvalidOperationException("The bulk transport mapping is unavailable.");
        if (host == null)
            throw new InvalidOperationException("The bulk transport host is unavailable.");
        if (!IsActiveTransport(transport))
        {
            // A wrapper may have different lane semantics. Keep ordinary connection
            // setup intact, but never advertise the singleton's pool for that path.
            _preparedHostPointer = IntPtr.Zero;
            _pendingHostPointer = IntPtr.Zero;
            PublishProfile();
            return;
        }
        // Peer arrays have fixed capacity. Never retrofit an unprepared connection,
        // even when the scene transport still owns a pool from a previous host.
        if (host.FirstPeer != null && !IsPreparedHost())
            throw new InvalidOperationException("Bulk lanes must be prepared before creating a peer.");
        var table = transport.channels;
        if (IsMappingValid())
        {
            _pendingHostPointer = host.Pointer;
            PublishProfile();
            return;
        }
        if (table.Length < 5 || table.Length >= BulkTransferProtocol.MaxPhysicalChannels ||
            table[0] != DeliveryMethod.ReliableOrdered || table[1] != DeliveryMethod.Unreliable ||
            table[2] != DeliveryMethod.Sequenced || table[3] != DeliveryMethod.ReliableSequenced ||
            table[4] != DeliveryMethod.ReliableUnordered)
            throw new InvalidOperationException("The configured transport mapping cannot provide Anomaly bulk channels safely.");
        for (int index = 0; index < table.Length; index++)
            if (!Enum.IsDefined(typeof(DeliveryMethod), table[index]))
                throw new InvalidOperationException("The transport contains an unsupported delivery method.");
        int first = Math.Max(table.Length, BulkTransferProtocol.FirstPhysicalChannel);
        int count = Math.Min(BulkTransferProtocol.MaxDomains, BulkTransferProtocol.MaxPhysicalChannels - first);
        if (count <= 0)
            throw new InvalidOperationException("The configured transport has no compatible bulk lane capacity.");
        var expanded = new DeliveryMethod[first + count];
        for (int index = 0; index < table.Length; index++) expanded[index] = table[index];
        for (int index = table.Length; index < first; index++) expanded[index] = DeliveryMethod.ReliableOrdered;
        ulong mask = 0;
        for (int index = first; index < expanded.Length; index++)
        {
            expanded[index] = DeliveryMethod.ReliableUnordered;
            mask |= 1UL << index;
        }
        transport.channels = new Il2CppStructArray<DeliveryMethod>(expanded);
        _transportPointer = transport.Pointer;
        _mapping = (DeliveryMethod[])expanded.Clone();
        _ownedMask = mask;
        _preparedHostPointer = IntPtr.Zero;
        _pendingHostPointer = host.Pointer;
        PublishProfile();
        MelonLogger.Msg($"Bulk transport prepared {count} reliable lanes at {first} on the existing game socket.");
    }

    internal static void Unavailable(Exception error)
    {
        // Keep ownership of an intact table so recreating a host does not append
        // another pool. Only a completed SetOptions can advertise it again.
        _preparedHostPointer = IntPtr.Zero;
        _pendingHostPointer = IntPtr.Zero;
        PublishProfile();
        MelonLogger.Warning("Bulk transport is unavailable: " + error.Message);
    }

    internal static void CompletePreparation()
    {
        var host = LiteNetLib4MirrorCore.Host;
        var transport = LiteNetLib4MirrorTransport.Singleton;
        if (!IsActiveTransport(transport))
        {
            _preparedHostPointer = IntPtr.Zero;
            _pendingHostPointer = IntPtr.Zero;
            PublishProfile();
            return;
        }
        if (host == null || host.Pointer != _pendingHostPointer || !IsMappingValid() ||
            host.ChannelsCount != transport.channels.Length)
            throw new InvalidOperationException("Native bulk channel capacity did not match the configured mapping.");
        _preparedHostPointer = host.Pointer;
        _pendingHostPointer = IntPtr.Zero;
        PublishProfile();
    }

    internal static bool IsOwnedTransport(Transport transport) =>
        transport != null && transport.Pointer == _transportPointer && IsPreparedHost();

    internal static bool IsOwnedChannel(int channel) => channel >= 0 && channel < BulkTransferProtocol.MaxPhysicalChannels &&
        (_ownedMask & (1UL << channel)) != 0 && IsPreparedHost();

    private static bool IsPreparedHost()
    {
        var host = LiteNetLib4MirrorCore.Host;
        var transport = LiteNetLib4MirrorTransport.Singleton;
        return IsActiveTransport(transport) && host != null && host.Pointer == _preparedHostPointer && IsMappingValid() &&
            host.ChannelsCount == transport.channels.Length;
    }

    private static bool IsActiveTransport(LiteNetLib4MirrorTransport transport)
    {
        var active = Transport.active;
        return transport != null && active != null && active.Pointer == transport.Pointer;
    }

    private static bool IsMappingValid()
    {
        var transport = LiteNetLib4MirrorTransport.Singleton;
        if (_ownedMask == 0 || transport == null || transport.Pointer != _transportPointer ||
            transport.channels == null || _mapping == null || transport.channels.Length != _mapping.Length ||
            transport.channels.Length > BulkTransferProtocol.MaxPhysicalChannels) return false;
        for (int channel = 0; channel < _mapping.Length; channel++)
            if (transport.channels[channel] != _mapping[channel]) return false;
        return true;
    }

    private static void PublishProfile()
    {
        AnomalyMessaging.IsBulkPhysicalChannel = IsOwnedChannel;
        FileTransferClient.ConfigureBulkProfile(BulkTransferProtocol.ProfileVersion, IsPreparedHost() ? _ownedMask : 0,
            IsPreparedHost);
    }
}
