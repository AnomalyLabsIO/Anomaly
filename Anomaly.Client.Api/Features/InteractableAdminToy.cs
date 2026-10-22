using Il2CppAdminToys;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only wrapper for client-observed InvisibleInteractableToy state.</summary>
public class InteractableAdminToy : AdminToy
{
    internal InteractableAdminToy(InvisibleInteractableToy toy) : base(toy) => InteractableBase = toy;

    public InvisibleInteractableToy InteractableBase { get; }
    public float InteractionDuration => InteractableBase.InteractionDuration;
    public bool IsLocked => InteractableBase.IsLocked;
    public bool CanSearch => InteractableBase.CanSearch;
}
