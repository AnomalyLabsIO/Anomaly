using LabApi.Features.Wrappers;

namespace Anomaly.Server.Content
{
    /// <summary>
    /// Optional server-authoritative logic for a custom item. Subclass and assign to
    /// <see cref="CustomItemDefinition.Logic"/>. Hooks run on the server only — never on the
    /// client — so the author's behaviour code never ships to players.
    /// </summary>
    public abstract class ItemLogicBase
    {
        /// <summary>
        /// Called when a player finishes using this custom item. Only fires for items whose
        /// base template actually raises the game's item-use events (usable items such as
        /// Medkit / Adrenaline / SCP-500); non-usable bases (e.g. Coin) never "use".
        /// </summary>
        public virtual void OnUsed(Player player) { }
    }
}
