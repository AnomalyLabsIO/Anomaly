using Anomaly.Client.Api.Features;
using Il2CppInteractables;
using Il2CppInteractables.Interobjects.DoorUtils;
using Il2CppInventorySystem.Items.Firearms.Attachments;
using Il2CppMapGeneration.Distributors;
using UnityEngine;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// A resolved local interaction after the local callback, before any server command.
/// This is a notification, not proof of server acceptance or a server-side veto point.
/// </summary>
public class PlayerInteractedEventArgs : PlayerEventArgs
{
    public PlayerInteractedEventArgs(Player player, InteractableCollider collider) : base(player)
    {
        Collider = collider;
        Target = collider != null ? collider.Target : null;
        ColliderId = collider != null ? collider.ColliderId : (byte)0;
    }

    public InteractableCollider Collider { get; }
    public MonoBehaviour Target { get; }
    public byte ColliderId { get; }
    public Door Door => Target == null ? null : Door.Get(Target.TryCast<DoorVariant>());
    public Features.Locker Locker => Target == null ? null : Features.Locker.Get(Target.TryCast<Il2CppMapGeneration.Distributors.Locker>());
    public Generator Generator => Target == null ? null : Generator.Get(Target.TryCast<Scp079Generator>());
    public Workstation Workstation => Target == null ? null : Workstation.Get(Target.TryCast<WorkstationController>());
}
