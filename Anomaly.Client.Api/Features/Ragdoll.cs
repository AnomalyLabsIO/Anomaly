using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2Cpp;
using Il2CppPlayerRoles;
using Il2CppPlayerRoles.Ragdolls;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="BasicRagdoll"/>. Exposes the death snapshot
/// (role, owner, nickname, creation time) for death-cam mods, ragdoll-cleanup
/// mods, and "who killed whom" replays.
/// </summary>
/// <remarks>
/// Ragdolls are <see cref="UnityEngine.MonoBehaviour"/>s spawned via Mirror;
/// the wrapper uses lazy creation via <see cref="Get"/>. Lifecycle events
/// (<c>RagdollEvents.Spawned</c> / <c>RagdollEvents.Removed</c>) live in the
/// new <see cref="Anomaly.Client.Api.Events.Handlers.RagdollEvents"/> handler.
/// </remarks>
[ScriptType]
public class Ragdoll
{
    private static readonly Dictionary<BasicRagdoll, Ragdoll> _ragdolls = new();

    private Ragdoll(BasicRagdoll ragdollBase)
    {
        Base = ragdollBase;
    }

    /// <summary>The underlying <see cref="BasicRagdoll"/>.</summary>
    public BasicRagdoll Base { get; }

    [ScriptVisible]
    public bool IsDestroyed => Base == null;

    public static bool TryGet(BasicRagdoll value, out Ragdoll wrapper)
    {
        wrapper = Get(value);
        return wrapper != null;
    }

    /// <summary>The role the dead player was when the ragdoll was created.</summary>
    [ScriptVisible]
    public RoleTypeId RoleType => Base != null ? Base.NetworkInfo.RoleType : RoleTypeId.None;

    /// <summary>The nickname of the dead player at time of death.</summary>
    [ScriptVisible]
    public string Nickname => Base != null ? Base.NetworkInfo.Nickname : null;

    /// <summary>The <see cref="ReferenceHub"/> that owned this ragdoll, or null.</summary>
    public ReferenceHub OwnerHub => Base?.NetworkInfo.OwnerHub;

    /// <summary>The owning player wrapper, or null if the hub is gone.</summary>
    public Player Owner
    {
        get
        {
            ReferenceHub hub = OwnerHub;
            return hub != null ? Player.Get(hub) : null;
        }
    }

    /// <summary>The death damage handler that produced this ragdoll.</summary>
    public Il2CppPlayerStatsSystem.DamageHandlerBase DamageHandler => Base?.NetworkInfo.Handler;

    /// <summary>Engine creation timestamp (DSP-time-style double).</summary>
    [ScriptVisible]
    public double CreationTime => Base != null ? Base.NetworkInfo.CreationTime : 0d;

    /// <summary>Seconds elapsed since the ragdoll was created.</summary>
    public float ExistenceTime => Base != null ? Base.NetworkInfo.ExistenceTime : 0f;

    /// <summary>True if the ragdoll has been frozen (no further physics).</summary>
    [ScriptVisible]
    public bool IsFrozen => Base != null && Base.Frozen;

    /// <summary>The ragdoll's center-of-mass transform.</summary>
    public Transform CenterPoint => Base?.CenterPoint;

    public Vector3 Position => Base != null && Base.transform != null ? Base.transform.position : Vector3.zero;

    public GameObject GameObject => Base != null ? Base.gameObject : null;

    /// <summary>All ragdolls currently in the scene.</summary>
    public static ReadOnlyCollection<Ragdoll> List
    {
        get
        {
            List<Ragdoll> result = new();
            foreach (BasicRagdoll r in UnityEngine.Object.FindObjectsOfType<BasicRagdoll>())
            {
                Ragdoll wrapper = Get(r);
                if (wrapper != null)
                    result.Add(wrapper);
            }
            return result.AsReadOnly();
        }
    }

    public static Ragdoll Get(BasicRagdoll ragdollBase)
    {
        if (ragdollBase == null) return null;
        if (_ragdolls.TryGetValue(ragdollBase, out Ragdoll wrapper))
            return wrapper;
        wrapper = new Ragdoll(ragdollBase);
        _ragdolls[ragdollBase] = wrapper;
        return wrapper;
    }

    internal static bool Unregister(BasicRagdoll ragdollBase)
    {
        if (ragdollBase == null) return false;
        return _ragdolls.Remove(ragdollBase);
    }

    internal static void ClearAll() => _ragdolls.Clear();

    public override string ToString() => $"Ragdoll({Nickname ?? "?"}, {RoleType})";
}
