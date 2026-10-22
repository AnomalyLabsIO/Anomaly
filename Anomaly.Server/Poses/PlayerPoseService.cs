using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Anomaly.Server.Api.Events.Arguments;
using Anomaly.Server.Api.Features;
using Anomaly.Server.Networking;
using Anomaly.Shared.Capabilities;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Poses;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using Mirror;
using PlayerRoles;
using ClientEvents = Anomaly.Server.Api.Events.Handlers.ClientEvents;

namespace Anomaly.Server.Poses;

/// <summary>Game-thread pose ownership and recipient-safe replication for one independent authorization domain.</summary>
internal sealed class PlayerPoseService
{
    private readonly bool _combat;
    private readonly CapabilityDescriptor Capability;
    internal PlayerPoseService(bool combat)
    {
        _combat = combat;
        Capability = new CapabilityDescriptor(combat ? "anomaly:player-combat-pose" : "anomaly:player-pose-renderer", 1, PoseWireCodec.CapabilityMinor);
    }
    private readonly PlayerPoseState State = new PlayerPoseState();
    private readonly PoseModelEpochs ModelEpochs = new PoseModelEpochs();
    private sealed class Model
    {
        internal ReferenceHub Hub;
        internal int Life;
        internal ulong Generation;
        internal sbyte Role;
        internal CombatPoseProjection Projection;
        internal bool ProjectionObserved;
    }
    private sealed class Recipient
    {
        internal ReferenceHub Hub;
        internal NetworkConnectionToClient Connection;
        internal readonly PoseTargetLedger Ledger = new PoseTargetLedger();
        internal Dictionary<uint, PoseTargetState> Targets => Ledger.Targets;
        internal PoseTrafficBudget Budget;
        internal bool Supported;
        internal bool NeedsReset = true;
        internal int Cursor;
    }
    private readonly Dictionary<uint, Model> Models = new Dictionary<uint, Model>();
    private readonly Dictionary<ReferenceHub, Recipient> Recipients = new Dictionary<ReferenceHub, Recipient>();
    private readonly HashSet<long> Owners = new HashSet<long>();
    private bool _initialized;
    private int _gameThread;
    private long _nextOwner;
    private ulong _nextBinding;
    private double _nextTick;
    private uint Epoch => ClientCapabilities.CurrentRoundEpoch;
    private int _deliveryDepth;
    private double Now => Math.Max(0d, NetworkTime.time);

    internal void Init()
    {
        if (_initialized) return;
        _gameThread = Thread.CurrentThread.ManagedThreadId;
        _initialized = true;
        StaticUnityMethods.OnLateUpdate += Tick;
        ClientEvents.CapabilitiesChanged += CapabilitiesChanged;
        PlayerEvents.Left += PlayerLeft;
        ServerEvents.WaitingForPlayers += ResetRound;
        ServerEvents.RoundRestarted += ResetRound;
    }

    internal void Disable()
    {
        try
        {
            if (_initialized)
            {
                State.Reset();
                foreach (Recipient recipient in Recipients.Values)
                    if (recipient.Supported) SendControls(recipient, new[] { new PoseControl { Operation = PoseControlOperation.ResetAll } });
            }
        }
        finally
        {
            StaticUnityMethods.OnLateUpdate -= Tick;
            ClientEvents.CapabilitiesChanged -= CapabilitiesChanged;
            PlayerEvents.Left -= PlayerLeft;
            ServerEvents.WaitingForPlayers -= ResetRound;
            ServerEvents.RoundRestarted -= ResetRound;
            _initialized = false;
            Models.Clear(); ModelEpochs.Clear(); Recipients.Clear(); Owners.Clear(); State.Reset();
            _nextTick = 0; _deliveryDepth = 0;
        }
    }

    internal void RequireGameThread()
    {
        if (_gameThread != 0 && Thread.CurrentThread.ManagedThreadId != _gameThread)
            throw new InvalidOperationException("Player pose APIs must run on the game thread.");
    }

    internal PlayerPoseScope CreateScope(string ownerId)
    {
        RequireGameThread();
        if (!_initialized) throw new InvalidOperationException("The player pose service is not active.");
        if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Length > 64 ||
            ownerId.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == '_')) ||
            ownerId == "anomaly" || ownerId.StartsWith("anomaly.", StringComparison.Ordinal))
            throw new ArgumentException("A non-reserved lowercase plugin namespace is required.", nameof(ownerId));
        if (Owners.Count >= 128) throw new InvalidOperationException("The pose owner limit is reached.");
        long id = checked(++_nextOwner);
        Owners.Add(id);
        return new PlayerPoseScope(this, id, ownerId);
    }

    internal bool TryCreate(long owner, Player player, PoseFrame frame, PlayerPoseOptions options,
        out PlayerPoseOverride handle, out PoseFailure failure)
    {
        RequireGameThread(); handle = null;
        if (!_initialized) { failure = PoseFailure.NotInitialized; return false; }
        if (!Owners.Contains(owner)) { failure = PoseFailure.Disposed; return false; }
        ReferenceHub hub = player?.ReferenceHub;
        if (hub == null || hub.netId == 0 || hub.roleManager?.CurrentRole == null) { failure = PoseFailure.NoPlayer; return false; }
        Model model = EnsureModel(hub);
        if (!ValidateFrame(model, frame, out failure)) return false;
        PlayerPoseOptions copy;
        try { copy = (options ?? new PlayerPoseOptions()).Copy(); }
        catch (ArgumentException) { failure = PoseFailure.InvalidFrame; return false; }
        if (!State.TryCreate(owner, hub.netId, model.Generation, frame, copy, Now, out ulong id))
        { failure = PoseFailure.LimitExceeded; return false; }
        handle = new PlayerPoseOverride(this, owner, id); failure = PoseFailure.None; return true;
    }

    internal bool IsValid(long owner, ulong id)
    {
        RequireGameThread();
        State.Expire(Now);
        return _initialized && Owners.Contains(owner) && State.TryGet(id, owner, out var entry) &&
            Models.TryGetValue(entry.NetId, out Model model) && model.Generation == entry.ModelGeneration &&
            model.Hub != null && model.Hub.roleManager?.CurrentRole?.UniqueLifeIdentifier == model.Life;
    }
    internal bool TryUpdate(long owner, ulong id, PoseFrame frame, out PoseFailure failure)
    {
        if (!IsValid(owner, id)) { failure = PoseFailure.ModelChanged; return false; }
        State.TryGet(id, owner, out var entry);
        if (!ValidateFrame(Models[entry.NetId], frame, out failure)) return false;
        State.Update(entry, frame, Now); failure = PoseFailure.None; return true;
    }
    internal bool TryGetState(long owner, ulong id, out PoseFrame frame)
    {
        frame = null;
        if (!IsValid(owner, id) || !State.TryGet(id, owner, out var entry)) return false;
        frame = PlayerPoseState.Copy(entry.Frame); return true;
    }
    internal void ResetInterpolation(long owner, ulong id)
    {
        if (!IsValid(owner, id) || !State.TryGet(id, owner, out var entry))
            throw new InvalidOperationException("The pose handle is no longer valid.");
        State.ResetInterpolation(entry.NetId);
    }
    internal void Remove(long owner, ulong id) { State.Remove(id, owner); FlushControls(); }
    internal void RemoveOwner(long owner) { Owners.Remove(owner); State.RemoveOwner(owner); FlushControls(); }

    private bool ValidateFrame(Model model, PoseFrame frame, out PoseFailure failure)
    {
        if (!PoseRigCatalog.TryGetForRole(model.Role, out var rig)) { failure = PoseFailure.UnsupportedRig; return false; }
        if (frame == null || frame.RigId != rig.Id || frame.SchemaVersion != rig.Version ||
            frame.Bones.Any(bone => !rig.TryGetBone(bone.BoneId, out _)))
        { failure = PoseFailure.InvalidFrame; return false; }
        failure = PoseFailure.None; return true;
    }

    internal void ModelInitialized(ReferenceHub hub)
    {
        if (!_initialized || hub == null || hub.netId == 0 || hub.roleManager?.CurrentRole == null) return;
        if (Models.TryGetValue(hub.netId, out Model existing) && existing.Hub == hub &&
            existing.Life == hub.roleManager.CurrentRole.UniqueLifeIdentifier) return;
        State.RemovePlayer(hub.netId);
        var role = hub.roleManager.CurrentRole;
        Models[hub.netId] = new Model { Hub = hub, Life = role.UniqueLifeIdentifier,
            Generation = ModelEpochs.Observe(hub.netId, hub, role.UniqueLifeIdentifier), Role = (sbyte)role.RoleTypeId };
        FlushControls();
    }

    private Model EnsureModel(ReferenceHub hub)
    {
        if (!Models.TryGetValue(hub.netId, out Model model) || model.Hub != hub ||
            model.Life != hub.roleManager.CurrentRole.UniqueLifeIdentifier)
        {
            ModelInitialized(hub);
            model = Models[hub.netId];
        }
        return model;
    }

    /// <summary>Called only after the native packed role bytes were successfully enqueued.</summary>
    internal void RoleDelivered(uint targetId, uint receiverId, sbyte deliveredRole, int nativeLife)
    {
        if (!_initialized || !ReferenceHub.TryGetHubNetID(receiverId, out ReferenceHub receiver) ||
            !ReferenceHub.TryGetHubNetID(targetId, out ReferenceHub target) || receiver.connectionToClient == null) return;
        Recipient recipient = GetRecipient(receiver);
        PoseTargetState entry = recipient.Ledger.GetOrAdd(targetId, out bool resetRequired);
        recipient.NeedsReset |= resetRequired;
        entry.Visible = receiver == target;
        entry.Retired = false;
        entry.Delivery = null;
        Model model = EnsureModel(target);
        // Never attach metadata from a newer life to bytes serialized for an earlier life.
        if (nativeLife == model.Life && PoseRigCatalog.TryGetForRole(deliveredRole, out var rig))
            entry.Delivery = new PoseBinding { NetId = targetId, ModelGeneration = model.Generation,
                BindingRevision = checked(++_nextBinding), RoleId = deliveredRole, RigId = rig.Id, SchemaVersion = rig.Version };
        if (recipient.Supported) FlushRecipientControls(recipient);
    }

    internal void BeginRoleDelivery() => _deliveryDepth++;
    internal void EndRoleDelivery()
    {
        if (--_deliveryDepth == 0) FlushControls();
    }

    internal void ObserveVisibility(ReferenceHub receiver, ReferenceHub target, bool visible)
    {
        if (!_initialized || receiver == null || target == null || !Recipients.TryGetValue(receiver, out Recipient recipient) ||
            !recipient.Targets.TryGetValue(target.netId, out PoseTargetState entry)) return;
        entry.Visible = visible || receiver == target;
    }

    internal void RootDiscontinuity(ReferenceHub hub)
    {
        if (!_initialized || hub == null) return;
        State.ResetInterpolation(hub.netId);
        FlushControls();
    }

    internal void RemovePlayer(ReferenceHub hub)
    {
        if (hub == null) return;
        State.RemovePlayer(hub.netId); Models.Remove(hub.netId); ModelEpochs.Remove(hub.netId); Recipients.Remove(hub);
        foreach (Recipient recipient in Recipients.Values) recipient.Ledger.Retire(hub.netId);
        FlushControls();
        foreach (Recipient recipient in Recipients.Values) recipient.Ledger.Prune(hub.netId);
    }

    private Recipient GetRecipient(ReferenceHub hub)
    {
        if (!Recipients.TryGetValue(hub, out Recipient recipient) || recipient.Connection != hub.connectionToClient)
        {
            recipient = new Recipient { Hub = hub, Connection = hub.connectionToClient, Budget = PlayerPoseRuntime.GetBudget(hub.connectionToClient) };
            Recipients[hub] = recipient;
        }
        return recipient;
    }
    private void CapabilitiesChanged(ClientEventArgs args)
    {
        if (args.Player?.ReferenceHub == null) return;
        Recipient recipient = GetRecipient(args.Player.ReferenceHub);
        bool supported = ClientCapabilities.Supports(args.Player, Capability);
        if (recipient.Supported == supported) return;
        if (recipient.Supported && !supported)
            SendControls(recipient, new[] { new PoseControl { Operation = PoseControlOperation.ResetAll } });
        recipient.Supported = supported;
        recipient.NeedsReset = true;
        foreach (PoseTargetState target in recipient.Targets.Values) { target.SentBinding = null; target.Active = false; }
        if (supported) FlushRecipientControls(recipient);
    }
    private void PlayerLeft(LabApi.Events.Arguments.PlayerEvents.PlayerLeftEventArgs args) => RemovePlayer(args.Player?.ReferenceHub);
    private void ResetRound()
    {
        State.Reset(); Models.Clear(); ModelEpochs.Clear();
        foreach (Recipient recipient in Recipients.Values)
        {
            recipient.NeedsReset = true;
            recipient.Ledger.Reset();
            if (recipient.Supported) FlushRecipientControls(recipient);
        }
    }

    private void Tick()
    {
        if (!_initialized || !NetworkServer.active || Now < _nextTick) return;
        _nextTick = Now + .05d;
        State.Expire(Now);
        foreach (Recipient recipient in Recipients.Values.ToArray())
        {
            if (recipient.Hub == null || recipient.Hub.connectionToClient != recipient.Connection) { Recipients.Remove(recipient.Hub); continue; }
            if (!recipient.Supported) continue;
            try { FlushRecipientControls(recipient); SendSamples(recipient); }
            catch (Exception exception) { Utils.OffThreadLogger.Warn("Pose presentation update failed: " + exception.Message); }
        }
    }

    private void FlushControls()
    {
        if (!_initialized || _deliveryDepth != 0) return;
        foreach (Recipient recipient in Recipients.Values.ToArray())
            if (recipient.Supported) FlushRecipientControls(recipient);
    }

    private bool CanPose(PoseTargetState target, out PlayerPoseState.Composed composed)
    {
        composed = null;
        if (!target.Visible || target.Delivery == null || !Models.TryGetValue(target.Delivery.NetId, out Model model) ||
            model.Hub == null || model.Generation != target.Delivery.ModelGeneration || model.Role != target.Delivery.RoleId)
            return false;
        composed = State.Compose(target.Delivery.NetId, Now);
        return composed.Frame != null && composed.Frame.RigId == target.Delivery.RigId;
    }

    private void FlushRecipientControls(Recipient recipient)
    {
        if (!recipient.Supported || _deliveryDepth != 0) return;
        if (recipient.NeedsReset)
        {
            if (!SendControls(recipient, new[] { new PoseControl { Operation = PoseControlOperation.ResetAll } })) return;
            recipient.NeedsReset = false;
        }
        foreach (var pair in recipient.Targets.ToArray())
        {
            PoseTargetState target = pair.Value;
            bool available = CanPose(target, out var composed);
            bool sameBinding = target.Delivery != null && target.Delivery.Matches(target.SentBinding);
            var controls = new List<PoseControl>();
            if (target.Active && (!available || !sameBinding))
                controls.Add(new PoseControl { Operation = PoseControlOperation.Clear, Binding = target.SentBinding.Clone(), OverrideGeneration = target.OverrideGeneration });
            if (target.Delivery != null && !sameBinding)
                controls.Add(new PoseControl { Operation = PoseControlOperation.Bind, Binding = target.Delivery.Clone() });
            bool activate = available && (!target.Active || !sameBinding || target.CompositionRevision != composed.Revision);
            ulong generation = activate ? checked(target.OverrideGeneration + 1) : target.OverrideGeneration;
            if (activate)
                controls.Add(new PoseControl { Operation = PoseControlOperation.Activate, Binding = target.Delivery.Clone(),
                    OverrideGeneration = generation, Sequence = composed.Sequence, ServerTime = composed.ServerTime, Frame = composed.Frame });
            if (controls.Count == 0)
            {
                recipient.Ledger.Prune(pair.Key);
                continue;
            }
            if (!SendControls(recipient, controls)) return;
            target.SentBinding = target.Delivery?.Clone();
            target.Active = available;
            if (activate)
            {
                target.OverrideGeneration = generation;
                target.CompositionRevision = composed.Revision;
                target.SampleSequence = composed.Sequence;
            }
            recipient.Ledger.Prune(pair.Key);
        }
    }

    private bool SendControls(Recipient recipient, IEnumerable<PoseControl> controls)
    {
        var message = new PlayerPoseControlMessage { RoundEpoch = Epoch };
        message.Controls.AddRange(controls);
        return SendBudgeted(recipient, message);
    }

    private bool SendBudgeted(Recipient recipient, IAnomalyMessage message)
    {
        message = ToWire(message);
        if (!AnomalyMessaging.TryGetPayloadBudget(recipient.Hub, message, out var capacity)) return false;
        int length;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        { message.Serialize(writer); writer.Flush(); length = checked((int)stream.Length); }
        if (length > capacity.MaxPayloadBytes) return false;
        int packetCount = Math.Max(1, (length + Math.Max(1, capacity.MaxUnfragmentedPayloadBytes) - 1) / Math.Max(1, capacity.MaxUnfragmentedPayloadBytes));
        // Reserve more than the largest supported envelope/framing overhead per fragment.
        if (!recipient.Budget.TryCharge(Now, packetCount, length + packetCount * 128)) return false;
        return AnomalyMessaging.TrySendOptionalToClient(recipient.Hub, message);
    }

    private void SendSamples(Recipient recipient)
    {
        if (recipient.NeedsReset || !AnomalyMessaging.TryGetPayloadBudget(recipient.Hub, ToWire(new PlayerPoseSampleMessage()), out var budget)) return;
        int maximum = Math.Min(budget.MaxPayloadBytes, budget.MaxUnfragmentedPayloadBytes);
        if (maximum < PoseWireCodec.MessageHeaderBytes + PoseWireCodec.SampleChunkHeaderBytes + 17) return;
        PoseTargetState[] targets = recipient.Targets.Values.Where(target => target.Active).ToArray();
        if (targets.Length == 0) return;
        int start = recipient.Cursor % targets.Length;
        var offers = new List<PoseSamplePacketizer.Offer>();
        for (int offset = 0; offset < targets.Length; offset++)
        {
            PoseTargetState target = targets[(start + offset) % targets.Length];
            if (!CanPose(target, out var frame) || !target.Delivery.Matches(target.SentBinding) ||
                target.CompositionRevision != frame.Revision || target.SampleSequence == frame.Sequence) continue;
            List<PoseSampleChunk> chunks = PoseSamplePacketizer.Chunk(target.Delivery, target.OverrideGeneration,
                frame.Sequence, frame.ServerTime, Project(target.Delivery, frame.Frame), maximum);
            if (chunks.Count == 0) continue;
            offers.Add(new PoseSamplePacketizer.Offer { Target = target, Sequence = frame.Sequence, Chunks = chunks,
                NextCursor = (start + offset + 1) % targets.Length });
        }
        if (!PoseSamplePacketizer.TrySelect(Epoch, offers, maximum, recipient.Budget, Now, out var selection, _combat)) return;
        foreach (var packet in selection.Packets)
            if (!AnomalyMessaging.TrySendOptionalToClient(recipient.Hub, ToWire(packet))) return;
        foreach (var work in selection.Offers) work.Target.SampleSequence = work.Sequence;
        recipient.Cursor = selection.Offers[selection.Offers.Count - 1].NextCursor;
    }
    private PoseFrame Project(PoseBinding binding, PoseFrame frame)
    {
        if (!_combat || frame == null || binding == null || !Models.TryGetValue(binding.NetId, out Model model)) return frame;
        if (model.Projection == null || !model.Projection.IsCurrent(model.Hub))
        {
            CombatPoseProjection previous = model.Projection;
            CombatPoseProjection.TryCreate(model.Hub, out model.Projection);
            if (model.ProjectionObserved && (previous != null || model.Projection != null))
                foreach (Recipient recipient in Recipients.Values)
                    if (recipient.Targets.TryGetValue(binding.NetId, out PoseTargetState target)) target.CompositionRevision = 0;
            model.ProjectionObserved = true;
        }
        return model.Projection?.Project(frame) ?? frame;
    }

    private IAnomalyMessage ToWire(IAnomalyMessage message)
    {
        if (!_combat) return message;
        if (message is PlayerPoseControlMessage control)
            return new PlayerCombatPoseControlMessage { RoundEpoch = control.RoundEpoch, Controls = control.Controls.Select(item =>
                new PoseControl { Operation = item.Operation, Binding = item.Binding, OverrideGeneration = item.OverrideGeneration,
                    Sequence = item.Sequence, ServerTime = item.ServerTime, Frame = Project(item.Binding, item.Frame) }).ToList() };
        if (message is PlayerPoseSampleMessage sample)
            return new PlayerCombatPoseSampleMessage { RoundEpoch = sample.RoundEpoch, Chunks = sample.Chunks };
        throw new ArgumentException("Unsupported pose service message.", nameof(message));
    }

}
