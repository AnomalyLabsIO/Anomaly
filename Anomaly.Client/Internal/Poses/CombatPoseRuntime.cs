using System.Diagnostics;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Poses;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Patches.Poses;
using Anomaly.Shared.Capabilities;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Poses;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Internal.Poses;

internal static class CombatPoseRuntime
{
    private static readonly PoseReceiver Receiver = new();
    private static readonly Dictionary<uint, CombatPoseRigBinding> Models = new();
    private static readonly Dictionary<uint, PoseBinding> WaitingBindings = new();
    private static readonly Dictionary<uint, List<PoseControl>> WaitingControls = new();
    private static readonly HashSet<uint> BoundPlayers = new();
    private static readonly Dictionary<uint, PoseBinding> BoundBindings = new();
    private static IDisposable _capability;
    private static bool _initialized;
    private static uint _epoch;
    internal static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    internal static void Initialize()
    {
        if (_initialized) return;
        CombatQueryPatches.VerifyInstalled();
        AnomalyMessaging.MessageReceived += OnMessage;
        ClientEvents.Disconnected += OnDisconnected;
        ClientEvents.Connected += OnConnected;
        try
        {
            _capability = ClientCapabilities.Register(new CapabilityDescriptor("anomaly:player-combat-pose", 1, PoseWireCodec.CapabilityMinor));
            _initialized = true;
        }
        catch
        {
            Shutdown();
            throw;
        }
    }

    internal static void Shutdown()
    {
        _initialized = false;
        try { _capability?.Dispose(); }
        finally
        {
            _capability = null;
            AnomalyMessaging.MessageReceived -= OnMessage;
            ClientEvents.Disconnected -= OnDisconnected;
            ClientEvents.Connected -= OnConnected;
            Reset();
        }
    }

    private static void OnConnected(ClientConnectedEventArgs _) => Reset();
    private static void OnDisconnected(ClientDisconnectedEventArgs _) => Reset();

    private static void Reset()
    {
        foreach (var model in Models.Values)
            try { model.Dispose(); } catch (Exception exception) { MelonLogger.Error("Combat model cleanup failed: " + exception); }
        Models.Clear();
        WaitingBindings.Clear();
        WaitingControls.Clear();
        BoundPlayers.Clear();
        BoundBindings.Clear();
        _epoch = 0;
        Receiver.Reset(0);
    }

    private static void OnMessage(IAnomalyMessage message)
    {
        if (!_initialized) return;
        uint epoch = message is PlayerCombatPoseControlMessage control ? control.RoundEpoch : message is PlayerCombatPoseSampleMessage sample ? sample.RoundEpoch : 0;
        if (epoch == 0 || epoch != ClientCapabilities.CurrentRoundEpoch) return;
        try
        {
            if (_epoch != epoch)
            {
                Reset();
                _epoch = epoch;
                Receiver.Reset(epoch);
            }
            if (message is PlayerCombatPoseControlMessage controls)
            {
                foreach (PoseControl item in controls.Controls)
                {
                    if (item.Operation == PoseControlOperation.ResetAll)
                    {
                        Reset();
                        _epoch = epoch;
                        Receiver.Reset(epoch);
                        continue;
                    }
                    uint netId = item.Binding.NetId;
                    if (item.Operation == PoseControlOperation.Bind)
                    {
                        if (!PoseReceiptLedger.Associate(item.Binding, combat: true)) continue;
                        if (BoundBindings.TryGetValue(netId, out var bound) && bound.Matches(item.Binding)) continue;
                        if (WaitingBindings.TryGetValue(netId, out var waiting) && waiting.Matches(item.Binding)) continue;
                        Invalidate(netId);
                        WaitingBindings[netId] = item.Binding.Clone();
                        TryBind(netId);
                    }
                    else if (WaitingBindings.TryGetValue(netId, out PoseBinding binding) && binding.Matches(item.Binding))
                    {
                        if (!WaitingControls.TryGetValue(netId, out var pending)) WaitingControls[netId] = pending = new List<PoseControl>();
                        if (pending.Count >= 64) { Invalidate(netId); continue; }
                        pending.Add(item);
                    }
                    else Receiver.Apply(item, Now);
                }
            }
            else if (message is PlayerCombatPoseSampleMessage samples)
                foreach (PoseSampleChunk chunk in samples.Chunks) Receiver.Apply(chunk, Now);
        }
        catch (Exception ex) { Fail(ex); }
    }

    internal static void Invalidate(uint netId)
    {
        try { if (Models.Remove(netId, out var model)) model.Dispose(); }
        finally
        {
            Receiver.Invalidate(netId);
            WaitingBindings.Remove(netId);
            WaitingControls.Remove(netId);
            BoundPlayers.Remove(netId);
            BoundBindings.Remove(netId);
        }
    }

    internal static void ReleaseModel(CharacterModel model)
    {
        if (model == null) return;
        foreach (var entry in Models.ToArray())
            if (entry.Value.Model != null && entry.Value.Model.Pointer == model.Pointer) Invalidate(entry.Key);
    }

    internal static void Forget(uint netId)
    {
        try { Invalidate(netId); }
        finally { Receiver.Forget(netId); }
    }

    internal static void TryBind(uint netId)
    {
        if (!WaitingBindings.TryGetValue(netId, out var binding) || !PoseReceiptLedger.IsInitialized(binding, combat: true)) return;
        if (!CombatPoseRigBinding.TryCreate(Player.Get(netId), out var model) || model.Rig.Id != binding.RigId || model.Rig.Version != binding.SchemaVersion) return;
        if (!Receiver.Bind(binding)) { model.Dispose(); Invalidate(netId); return; }
        Models[netId] = model;
        BoundPlayers.Add(netId);
        BoundBindings[netId] = binding.Clone();
        WaitingBindings.Remove(netId);
        if (WaitingControls.Remove(netId, out var pending))
            foreach (var control in pending) Receiver.Apply(control, Now);
    }

    internal static IDisposable BeginQuery()
    {
        if (!_initialized) return null;
        try
        {
            if (_epoch != ClientCapabilities.CurrentRoundEpoch) Reset();
        }
        catch (Exception exception) { Fail(exception); return null; }
        try
        {
            foreach (uint netId in WaitingBindings.Keys.ToArray()) TryBind(netId);
            return PoseMutationScope.Begin(journal =>
            {
                foreach (uint netId in BoundPlayers.ToArray())
                {
                    if (!Models.TryGetValue(netId, out var model) || !model.IsCurrent)
                    { Invalidate(netId); continue; }
                    if (!Receiver.TryGetForCombat(netId, Now, out PoseFrame frame, out PoseBinding binding) ||
                        !PoseReceiptLedger.IsInitialized(binding, combat: true)) continue;
                    IDisposable transaction = model.BeginQuery(frame);
                    if (transaction != null) journal.AddRestore(transaction.Dispose);
                }
            }, Physics.SyncTransforms);
        }
        catch (Exception exception) { Fail(exception); return null; }
    }

    private static void Fail(Exception exception)
    {
        try { MelonLogger.Error($"Player combat pose bridge disabled: {exception}"); } catch { }
        try { Shutdown(); }
        catch (Exception cleanup) { try { MelonLogger.Error("Combat bridge cleanup failed: " + cleanup); } catch { } }
    }
}
