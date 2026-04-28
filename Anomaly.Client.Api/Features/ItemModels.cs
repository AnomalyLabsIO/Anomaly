using System.Collections.Generic;
using Il2Cpp;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;
using Il2CppInventorySystem.Items.Pickups;
using Il2CppMirror;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Static access to item pickup prefabs by <see cref="ItemType"/>, plus a
/// helper for instantiating a pickup as a pure-visual GameObject (physics,
/// networking, and pickup behaviour stripped). Use cases include cosmetic
/// attachments to player anchor points, UI previews, and custom rendering
/// that should not interact with the game world.
/// </summary>
/// <remarks>
/// The prefab source is <see cref="InventoryItemLoader.AvailableItems"/>.
/// Each <see cref="ItemBase"/> carries its drop model in
/// <see cref="ItemBase.PickupDropModel"/> — the same prefab used when an
/// item is dropped to the floor by a player.
/// </remarks>
public static class ItemModels
{
    /// <summary>
    /// Returns the <see cref="ItemPickupBase"/> prefab for the given
    /// <paramref name="type"/>, or <c>null</c> if the loader has no entry.
    /// </summary>
    public static ItemPickupBase GetPickupPrefab(ItemType type)
    {
        TryGetPickupPrefab(type, out var prefab);
        return prefab;
    }

    /// <summary>
    /// Returns <see cref="GetPickupPrefab(ItemType)"/>'s <c>gameObject</c>,
    /// or <c>null</c> if no entry is found.
    /// </summary>
    public static GameObject GetPickupPrefabGameObject(ItemType type)
    {
        var prefab = GetPickupPrefab(type);
        return prefab != null ? prefab.gameObject : null;
    }

    public static bool TryGetPickupPrefab(ItemType type, out ItemPickupBase prefab)
    {
        prefab = null;
        var available = InventoryItemLoader.AvailableItems;
        if (available == null)
            return false;
        if (!available.TryGetValue(type, out var itemBase) || itemBase == null)
            return false;

        prefab = itemBase.PickupDropModel;
        return prefab != null;
    }

    /// <summary>
    /// Returns every <see cref="Renderer"/> under the pickup prefab for the
    /// given <paramref name="type"/>, or an empty list if the prefab is not
    /// found. The list is built fresh each call (do not assume identity).
    /// </summary>
    public static IReadOnlyList<Renderer> GetRenderers(ItemType type)
    {
        var prefab = GetPickupPrefabGameObject(type);
        if (prefab == null)
            return System.Array.Empty<Renderer>();
        return CollectRenderers(prefab);
    }

    /// <summary>
    /// Instantiates a copy of the pickup prefab for <paramref name="type"/>
    /// with all physics, networking, and pickup-behaviour components
    /// stripped: <see cref="ItemPickupBase"/>, <see cref="NetworkBehaviour"/>,
    /// <see cref="NetworkIdentity"/>, and <see cref="Rigidbody"/> are
    /// destroyed; every <see cref="Collider"/> is disabled. The result is a
    /// pure-visual GameObject suitable for parenting under a player anchor
    /// or UI element. Returns <c>null</c> if the prefab is not found.
    /// </summary>
    /// <param name="type">The item type to clone.</param>
    /// <param name="parent">
    /// Optional parent transform. When provided the clone is reparented and
    /// reset to local origin / identity rotation. When <c>null</c> the clone
    /// is left in world space at the prefab's origin.
    /// </param>
    public static GameObject InstantiateVisual(ItemType type, Transform parent = null)
    {
        var prefab = GetPickupPrefabGameObject(type);
        if (prefab == null)
            return null;
        return InstantiateVisual(prefab, parent);
    }

    /// <inheritdoc cref="InstantiateVisual(ItemType, Transform)" />
    public static GameObject InstantiateVisual(GameObject pickupPrefab, Transform parent = null)
    {
        if (pickupPrefab == null)
            return null;

        var clone = UnityEngine.Object.Instantiate(pickupPrefab);
        StripPickupBehaviour(clone);

        if (parent != null)
        {
            clone.transform.SetParent(parent, worldPositionStays: false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
        }

        return clone;
    }

    private static List<Renderer> CollectRenderers(GameObject root)
    {
        var found = root.GetComponentsInChildren<Renderer>(true);
        var list = new List<Renderer>(found != null ? found.Length : 0);
        if (found == null)
            return list;
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
                list.Add(found[i]);
        }
        return list;
    }

    private static void StripPickupBehaviour(GameObject root)
    {
        // ItemPickupBase carries the pickup contract; once destroyed the
        // clone no longer registers with InventoryItemLoader / search code.
        var pickups = root.GetComponentsInChildren<ItemPickupBase>(true);
        if (pickups != null)
        {
            for (int i = 0; i < pickups.Length; i++)
            {
                if (pickups[i] != null)
                    UnityEngine.Object.Destroy(pickups[i]);
            }
        }

        // Mirror's NetworkBehaviour / NetworkIdentity will try to register on
        // OnEnable. Destroying them before the next frame prevents that.
        var behaviours = root.GetComponentsInChildren<NetworkBehaviour>(true);
        if (behaviours != null)
        {
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null)
                    UnityEngine.Object.Destroy(behaviours[i]);
            }
        }

        var identities = root.GetComponentsInChildren<NetworkIdentity>(true);
        if (identities != null)
        {
            for (int i = 0; i < identities.Length; i++)
            {
                if (identities[i] != null)
                    UnityEngine.Object.Destroy(identities[i]);
            }
        }

        // Physics: a kinematic Rigidbody is still affected by joints / forces
        // and adds simulation cost. Removing it entirely is cheapest.
        var bodies = root.GetComponentsInChildren<Rigidbody>(true);
        if (bodies != null)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] != null)
                    UnityEngine.Object.Destroy(bodies[i]);
            }
        }

        // Colliders: disable rather than destroy so the prefab's authored
        // bounds stay introspectable for any caller that wants to read them.
        var colliders = root.GetComponentsInChildren<Collider>(true);
        if (colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }
        }
    }
}
