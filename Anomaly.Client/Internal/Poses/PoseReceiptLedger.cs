using Anomaly.Client.Api.Features;
using Anomaly.Shared.Poses;
using Il2Cpp;
using Il2CppMirror;
using Il2CppPlayerRoles;

namespace Anomaly.Client.Internal.Poses;

/// <summary>Pairs a reliable sideband with the exact preceding native role receipt, including queued readers.</summary>
internal static class PoseReceiptLedger
{
    internal sealed class Receipt
    {
        internal uint NetId;
        internal sbyte Role;
        internal NetworkReader Reader;
        internal int Start;
        internal bool Complete;
        internal bool Failed;
        internal int Life;
        internal IntPtr Model;
        internal bool Initialized;
        internal PoseBinding Binding;
        internal PoseBinding CombatBinding;
    }

    internal sealed class ReadScope
    {
        internal Receipt Receipt;
        internal bool Owner;
        internal bool Closed;
    }

    private static readonly Dictionary<uint, Receipt> Latest = new();
    private static readonly List<Receipt> Active = new();
    private static bool _observing;

    internal static void Enable() => _observing = true;
    internal static void Disable() { _observing = false; Reset(); }

    internal static void Reset()
    {
        Latest.Clear();
        Active.Clear();
    }

    internal static ReadScope BeginRead(NetworkReader reader)
    {
        if (!_observing || reader == null) return null;
        int position = reader.Position;
        if (Active.Count > 0)
        {
            Receipt active = Active[Active.Count - 1];
            if (active.Reader.Pointer == reader.Pointer && active.Start == position)
                return new ReadScope { Receipt = active };
        }
        uint netId;
        sbyte role;
        try
        {
            netId = reader.ReadUInt();
            role = reader.ReadSByte();
        }
        finally { reader.Position = position; }
        if (netId == 0) return null;
        PoseRuntime.InvalidateModel(netId);
        if (!Latest.ContainsKey(netId) && Latest.Count >= 512) return null;
        // Retain the reader object, not just its reusable native pointer.
        var receipt = new Receipt { NetId = netId, Role = role, Reader = reader, Start = position };
        Latest[netId] = receipt;
        Active.Add(receipt);
        return new ReadScope { Receipt = receipt, Owner = true };
    }

    internal static void EndRead(ReadScope scope, bool success)
    {
        if (scope == null || scope.Closed) return;
        if (!success) scope.Receipt.Failed = true;
        if (!scope.Owner) return;
        scope.Closed = true;
        Active.Remove(scope.Receipt);
        scope.Receipt.Complete = success && !scope.Receipt.Failed;
        if (!scope.Receipt.Complete)
        {
            Invalidate(scope.Receipt.NetId);
            return;
        }
        PoseRuntime.TryBindModel(scope.Receipt.NetId);
    }

    internal static Receipt BeforeInitialize(ReferenceHub hub, RoleTypeId role, NetworkReader data)
    {
        if (!_observing || hub == null) return null;
        if (data != null && Latest.TryGetValue(hub.netId, out Receipt receipt) &&
            receipt.Role == (sbyte)role && receipt.Reader.Pointer == data.Pointer &&
            data.Position == receipt.Start + sizeof(uint) + sizeof(sbyte))
        {
            // An immediate native receipt is on our read stack. A deferred receipt must
            // still be the exact reader stored for this netId by the stock queue.
            bool immediate = Active.Contains(receipt);
            bool queued = PlayerRolesNetUtils.QueuedRoles.TryGetValue(hub.netId, out NetworkReader queuedReader) &&
                queuedReader != null && queuedReader.Pointer == data.Pointer;
            if (immediate || queued) return receipt;
        }
        Invalidate(hub.netId);
        return null;
    }

    internal static void AfterInitialize(ReferenceHub hub, Receipt receipt)
    {
        if (hub == null || receipt == null || !Latest.TryGetValue(hub.netId, out var latest) || !ReferenceEquals(latest, receipt)) return;
        Player player = Player.Get(hub);
        if (player == null || (sbyte)player.RoleType != receipt.Role || player.CharacterModel == null) return;
        receipt.Life = player.CurrentRole.UniqueLifeIdentifier;
        receipt.Model = player.CharacterModel.Pointer;
        receipt.Initialized = true;
        PoseRuntime.TryBindModel(hub.netId);
    }

    internal static void Invalidate(uint netId)
    {
        Latest.Remove(netId);
        PoseRuntime.InvalidateModel(netId);
    }

    internal static void Forget(uint netId)
    {
        Latest.Remove(netId);
        foreach (Receipt receipt in Active)
            if (receipt.NetId == netId) receipt.Failed = true;
        PoseRuntime.Forget(netId);
    }

    internal static bool Associate(PoseBinding binding, bool combat = false)
    {
        if (binding == null || !Latest.TryGetValue(binding.NetId, out var receipt) || !receipt.Complete ||
            receipt.Role != binding.RoleId || !PoseRigCatalog.TryGetForRole(binding.RoleId, out var rig) ||
            rig.Id != binding.RigId || rig.Version != binding.SchemaVersion) return false;
        PoseBinding previous = combat ? receipt.CombatBinding : receipt.Binding;
        if (previous != null && binding.BindingRevision <= previous.BindingRevision)
            return previous.Matches(binding);
        if (combat) receipt.CombatBinding = binding.Clone();
        else receipt.Binding = binding.Clone();
        return true;
    }

    internal static bool IsInitialized(PoseBinding binding, bool combat = false)
    {
        if (binding == null || !Latest.TryGetValue(binding.NetId, out var receipt) || !receipt.Complete ||
            !receipt.Initialized) return false;
        PoseBinding associated = combat ? receipt.CombatBinding : receipt.Binding;
        if (associated == null || !associated.Matches(binding)) return false;
        Player player = Player.Get(binding.NetId);
        return player != null && !player.IsDestroyed && player.CharacterModel != null &&
            player.CharacterModel.Pointer == receipt.Model && player.CurrentRole.UniqueLifeIdentifier == receipt.Life &&
            (sbyte)player.RoleType == binding.RoleId;
    }
}
