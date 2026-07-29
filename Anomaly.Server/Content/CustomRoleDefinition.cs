using PlayerRoles;
using UnityEngine;

namespace Anomaly.Server.Content
{
    /// <summary>
    /// Declarative definition of a custom role. Cloned from a vanilla FPC role template for
    /// movement/model plumbing; identity (id, name, colour, team) is overridden.
    /// </summary>
    public sealed class CustomRoleDefinition
    {
        /// <summary>Namespaced stable key, e.g. "mymod:warden". Required.</summary>
        public string Key { get; set; }

        /// <summary>Vanilla role whose template is cloned (should be a human FPC role).</summary>
        public RoleTypeId BaseTemplate { get; set; } = RoleTypeId.ClassD;

        /// <summary>Display name shown in-game (resolve via your i18n before assigning).</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>Engine team this role belongs to (drives win conditions / friendly fire).</summary>
        public Team Team { get; set; } = Team.ClassD;

        /// <summary>Nametag / UI colour.</summary>
        public Color Color { get; set; } = Color.white;

        /// <summary>Relative path to an AssetBundle file carrying the character model (optional).</summary>
        public string ModelBundlePath { get; set; }

        /// <summary>Asset name of the model GameObject within the bundle.</summary>
        public string ModelAssetName { get; set; } = "";

        /// <summary>Optional server-side logic (e.g. OnSpawned).</summary>
        public RoleLogicBase Logic { get; set; }
    }
}
