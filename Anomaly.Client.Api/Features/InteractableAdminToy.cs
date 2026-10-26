using Il2CppAdminToys;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only wrapper for client-observed InvisibleInteractableToy state.</summary>
[ScriptType]
public class InteractableAdminToy : AdminToy
{
    internal InteractableAdminToy(InvisibleInteractableToy toy) : base(toy) => InteractableBase = toy;

    public InvisibleInteractableToy InteractableBase { get; }
    public float InteractionDuration => InteractableBase.InteractionDuration;
    public bool IsLocked => InteractableBase.IsLocked;
    public bool CanSearch => InteractableBase.CanSearch;
}
