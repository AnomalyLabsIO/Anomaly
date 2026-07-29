using LabApi.Features.Wrappers;

namespace Anomaly.Server.Content
{
    /// <summary>
    /// Optional server-authoritative logic for a custom role. Subclass and assign to
    /// <see cref="CustomRoleDefinition.Logic"/>. Hooks run on the server only.
    /// </summary>
    public abstract class RoleLogicBase
    {
        /// <summary>Called right after a player spawns into this custom role.</summary>
        public virtual void OnSpawned(Player player) { }
    }
}
