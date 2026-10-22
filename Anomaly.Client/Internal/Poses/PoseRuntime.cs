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

namespace Anomaly.Client.Internal.Poses;

internal static class PoseRuntime
{
    private static readonly PoseReceiver Receiver = new();
    private static readonly Dictionary<uint, PoseRigBinding> Models = new();
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
        PoseReceiptPatches.VerifyInstalled();
        PoseReceiptLedger.Enable();
        AnomalyMessaging.MessageReceived += OnMessage;
        ClientEvents.Disconnected += OnDisconnected;
        ClientEvents.Connected += OnConnected;
        try
        {
            PosePresentationLoop.Install();
            _capability = ClientCapabilities.Register(new CapabilityDescriptor("anomaly:player-pose-renderer", 1, PoseWireCodec.CapabilityMinor));
            _initialized = true;
            try { CombatPoseRuntime.Initialize(); }
            catch (Exception exception) { MelonLogger.Error("Combat pose bridge unavailable: " + exception); }
        }
        catch
        {
            Shutdown();
            throw;
        }
    }

    internal static void Update()
    {
        if (_initialized && !PosePresentationLoop.Healthy()) Fail(new InvalidOperationException("The pose presentation loop stopped or was replaced."));
    }

    internal static void Shutdown()
    {
        try { CombatPoseRuntime.Shutdown(); }
        catch (Exception exception) { MelonLogger.Error("Combat bridge shutdown failed: " + exception); }
        _initialized = false;
        _capability?.Dispose();
        _capability = null;
        PoseReceiptLedger.Disable();
        AnomalyMessaging.MessageReceived -= OnMessage;
        ClientEvents.Disconnected -= OnDisconnected;
        ClientEvents.Connected -= OnConnected;
        Reset(true);
        PosePresentationLoop.Uninstall();
    }

    private static void OnConnected(ClientConnectedEventArgs _) => Reset(true);
    private static void OnDisconnected(ClientDisconnectedEventArgs _) => Reset(true);

    private static void Reset(bool receipts)
    {
        Restore();
        foreach (var model in Models.Values) model.Dispose();
        Models.Clear();
        WaitingBindings.Clear();
        WaitingControls.Clear();
        BoundPlayers.Clear();
        BoundBindings.Clear();
        _epoch = 0;
        Receiver.Reset(0);
        if (receipts) PoseReceiptLedger.Reset();
    }

    private static void OnMessage(IAnomalyMessage message)
    {
        if (!_initialized) return;
        uint epoch = message is PlayerPoseControlMessage control ? control.RoundEpoch : message is PlayerPoseSampleMessage sample ? sample.RoundEpoch : 0;
        if (epoch == 0 || epoch != ClientCapabilities.CurrentRoundEpoch) return;
        try
        {
            if (_epoch != epoch)
            {
                Reset(false);
                _epoch = epoch;
                Receiver.Reset(epoch);
            }
            if (message is PlayerPoseControlMessage controls)
            {
                foreach (PoseControl item in controls.Controls)
                {
                    if (item.Operation == PoseControlOperation.ResetAll)
                    {
                        Reset(false);
                        _epoch = epoch;
                        Receiver.Reset(epoch);
                        continue;
                    }
                    uint netId = item.Binding.NetId;
                    if (item.Operation == PoseControlOperation.Bind)
                    {
                        if (!PoseReceiptLedger.Associate(item.Binding)) continue;
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
            else if (message is PlayerPoseSampleMessage samples)
                foreach (PoseSampleChunk chunk in samples.Chunks) Receiver.Apply(chunk, Now);
        }
        catch (Exception ex) { Fail(ex); }
    }

    internal static void Invalidate(uint netId)
    {
        if (Models.Remove(netId, out var model)) model.Dispose();
        Receiver.Invalidate(netId);
        WaitingBindings.Remove(netId);
        WaitingControls.Remove(netId);
        BoundPlayers.Remove(netId);
        BoundBindings.Remove(netId);
    }

    internal static void ReleaseModel(CharacterModel model)
    {
        try { CombatPoseRuntime.ReleaseModel(model); }
        catch (Exception exception) { MelonLogger.Error("Combat model release failed: " + exception); }
        if (model == null) return;
        foreach (var entry in Models.ToArray())
            if (entry.Value.Model != null && entry.Value.Model.Pointer == model.Pointer) Invalidate(entry.Key);
    }

    internal static void Forget(uint netId)
    {
        try { CombatPoseRuntime.Forget(netId); }
        finally
        {
            try { Invalidate(netId); }
            finally { Receiver.Forget(netId); }
        }
    }

    internal static void InvalidateModel(uint netId)
    {
        try { Invalidate(netId); }
        finally { CombatPoseRuntime.Invalidate(netId); }
    }
    internal static void TryBindModel(uint netId) { TryBind(netId); CombatPoseRuntime.TryBind(netId); }

    internal static void TryBind(uint netId)
    {
        if (!WaitingBindings.TryGetValue(netId, out var binding) || !PoseReceiptLedger.IsInitialized(binding)) return;
        if (!PoseRigBinding.TryCreate(Player.Get(netId), out var model) || model.Rig.Id != binding.RigId || model.Rig.Version != binding.SchemaVersion) return;
        if (!Receiver.Bind(binding)) { model.Dispose(); Invalidate(netId); return; }
        Models[netId] = model;
        BoundPlayers.Add(netId);
        BoundBindings[netId] = binding.Clone();
        WaitingBindings.Remove(netId);
        if (WaitingControls.Remove(netId, out var pending))
            foreach (var control in pending) Receiver.Apply(control, Now);
    }

    internal static void Restore()
    {
        foreach (var model in Models.Values)
        {
            try { model.Restore(); }
            catch (Exception ex) { MelonLogger.Warning($"Pose restoration failed: {ex.Message}"); }
        }
    }

    internal static void Present()
    {
        if (!_initialized) return;
        if (_epoch != ClientCapabilities.CurrentRoundEpoch) Reset(false);
        try
        {
            foreach (uint netId in WaitingBindings.Keys.ToArray()) TryBind(netId);
            foreach (uint netId in BoundPlayers.ToArray())
            {
                if (!Models.TryGetValue(netId, out var model) || !model.IsCurrent)
                {
                    Invalidate(netId);
                    continue;
                }
                if (Receiver.TryGet(netId, Now, out PoseFrame frame, out PoseBinding binding) && PoseReceiptLedger.IsInitialized(binding)) model.Apply(frame);
                else model.Restore();
            }
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static void Fail(Exception exception)
    {
        MelonLogger.Error($"Player pose renderer disabled: {exception}");
        Shutdown();
    }
}
