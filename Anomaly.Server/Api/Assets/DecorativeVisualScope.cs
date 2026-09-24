using System;
using System.Collections.Generic;
using System.Threading;
using Anomaly.Server.Assets;
using Anomaly.Server.Api.Content;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using LabApi.Events.Handlers;
using UnityEngine;

namespace Anomaly.Server.Api.Assets;

/// <summary>Owns decorative client-only instances. Dispose from the owning LabAPI plugin's Disable method.</summary>
public sealed class DecorativeVisualScope : IDisposable
{
    private static readonly DecorativeVisualState State = new DecorativeVisualState();
    private sealed class Recipient
    {
        internal ulong Revision;
        internal readonly HashSet<uint> Instances = new HashSet<uint>();
    }
    private static readonly Dictionary<uint, Recipient> Recipients = new Dictionary<uint, Recipient>();
    private static long _nextOwner;
    private static bool _initialized;
    private static int _gameThread;
    private readonly long _owner = Interlocked.Increment(ref _nextOwner);
    private readonly string _namespace;
    private bool _disposed;

    public DecorativeVisualScope(string modNamespace)
    {
        RequireGameThread();
        if (!ModNamespace.IsValid(modNamespace) || ModNamespace.IsReserved(modNamespace)) throw new ArgumentException("A non-reserved mod namespace is required.", nameof(modNamespace));
        _namespace = modNamespace;
    }
    public uint Spawn(AssetAddress visualAddress, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        EnsureUsable();
        if (!visualAddress.Value.StartsWith("mods/" + _namespace + "/", StringComparison.Ordinal) ||
            !ServerAssetManager.TryGetAsset(visualAddress, out var asset) || asset.Kind != AssetKind.DecorativeVisual)
            throw new ArgumentException("Address must identify this namespace's indexed decorative visual.", nameof(visualAddress));
        var entry = State.Spawn(_owner, visualAddress, asset.IndexHash, asset.TriangleCount, Vector(position), QuaternionValues(rotation), Vector(scale));
        Broadcast(DecorativeVisualOperation.Upsert, entry.Instance, entry.IndexHash);
        return entry.Instance.Id;
    }
    public void Update(uint instanceId, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        EnsureUsable();
        var entry = State.Update(_owner, instanceId, Vector(position), QuaternionValues(rotation), Vector(scale));
        Broadcast(DecorativeVisualOperation.Upsert, entry.Instance, entry.IndexHash);
    }
    public bool Despawn(uint instanceId)
    {
        EnsureUsable();
        if (!State.Remove(_owner, instanceId)) return false;
        Broadcast(DecorativeVisualOperation.Remove, new DecorativeVisualInstance { Id = instanceId }, null);
        return true;
    }
    public void Dispose()
    {
        RequireGameThread();
        if (_disposed) return;
        _disposed = true;
        foreach (uint id in State.OwnedIds(_owner))
        {
            State.Remove(_owner, id);
            Broadcast(DecorativeVisualOperation.Remove, new DecorativeVisualInstance { Id = id }, null);
        }
    }
    internal static void Init()
    {
        if (_initialized) return;
        _initialized = true; _gameThread = Thread.CurrentThread.ManagedThreadId;
        ContentGrantGate.Activated += Activated;
        ContentGrantGate.Revoked += Revoked;
        ServerEvents.RoundRestarted += ClearRound;
    }
    internal static void Disable()
    {
        if (!_initialized) return;
        ClearRound();
        ContentGrantGate.Activated -= Activated;
        ContentGrantGate.Revoked -= Revoked;
        ServerEvents.RoundRestarted -= ClearRound;
        Recipients.Clear(); _initialized = false;
    }
    private static void ClearRound()
    {
        State.Clear(); Recipients.Clear();
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            if (ContentGrantGate.GetReadiness(hub.netId) == ContentReadiness.Activated)
                try { SendSnapshot(hub); }
                catch (Exception exception) { Utils.OffThreadLogger.Warn("Visual reset delivery failed: " + exception.Message); }
    }
    private static void Revoked(uint netId) => Recipients.Remove(netId);
    private static void Activated(uint netId)
    {
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            if (hub.netId == netId) { SendSnapshot(hub); return; }
    }
    private static void SendSnapshot(ReferenceHub hub)
    {
        var message = new DecorativeVisualSnapshotMessage { Generation = State.Generation, Revision = State.Revision };
        foreach (var entry in State.Entries)
            if (FileTransferSession.IsAssetApproved(hub, AssetAddress.Parse(entry.Instance.Address), AssetKind.DecorativeVisual, entry.IndexHash))
                message.Instances.Add(entry.Instance.Clone());
        if (!AnomalyMessaging.TrySendToClient(hub, message)) throw new InvalidOperationException("Visual snapshot delivery failed.");
        var recipient = new Recipient { Revision = message.Revision };
        foreach (var instance in message.Instances) recipient.Instances.Add(instance.Id);
        Recipients[hub.netId] = recipient;
    }
    private static void Broadcast(DecorativeVisualOperation operation, DecorativeVisualInstance instance, byte[] indexHash)
    {
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            if (ContentGrantGate.GetReadiness(hub.netId) != ContentReadiness.Activated || !Recipients.TryGetValue(hub.netId, out Recipient recipient)) continue;
            if (operation == DecorativeVisualOperation.Upsert && !FileTransferSession.IsAssetApproved(hub, AssetAddress.Parse(instance.Address), AssetKind.DecorativeVisual, indexHash)) continue;
            if (operation == DecorativeVisualOperation.Remove && !recipient.Instances.Contains(instance.Id)) continue;
            var message = new DecorativeVisualUpdateMessage { Generation = State.Generation, Revision = checked(recipient.Revision + 1), Operation = operation, Instance = instance.Clone() };
            try
            {
                if (!AnomalyMessaging.TrySendToClient(hub, message)) { Recipients.Remove(hub.netId); continue; }
                recipient.Revision = message.Revision;
                if (operation == DecorativeVisualOperation.Remove) recipient.Instances.Remove(instance.Id);
                else recipient.Instances.Add(instance.Id);
            }
            catch (Exception exception) { Recipients.Remove(hub.netId); Utils.OffThreadLogger.Warn("Visual update delivery failed: " + exception.Message); }
        }
    }
    private void EnsureUsable() { RequireGameThread(); if (_disposed) throw new ObjectDisposedException(nameof(DecorativeVisualScope)); if (!_initialized) throw new InvalidOperationException("Anomaly visual service is not active."); }
    private static void RequireGameThread() { if (_gameThread != 0 && Thread.CurrentThread.ManagedThreadId != _gameThread) throw new InvalidOperationException("Decorative visual APIs must run on the game thread."); }
    private static float[] Vector(Vector3 value) => new[] { value.x, value.y, value.z };
    private static float[] QuaternionValues(Quaternion value) => new[] { value.x, value.y, value.z, value.w };
}
