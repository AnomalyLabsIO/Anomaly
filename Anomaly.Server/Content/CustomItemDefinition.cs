namespace Anomaly.Server.Content
{
    /// <summary>
    /// Declarative definition of a custom item. The author fills this in (resolving
    /// <see cref="DisplayName"/> through their own i18n) and passes it to
    /// <see cref="Content.Register(CustomItemDefinition)"/>. Presentation assets are referenced
    /// by path relative to the server's configured asset directory.
    /// </summary>
    public sealed class CustomItemDefinition
    {
        /// <summary>Namespaced stable key, e.g. "mymod:magic_coin". Required.</summary>
        public string Key { get; set; }

        /// <summary>Vanilla item whose template is cloned for functional plumbing.</summary>
        public ItemType BaseTemplate { get; set; } = ItemType.Coin;

        /// <summary>Display name shown in-game (resolve via your i18n before assigning).</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>Optional item description line.</summary>
        public string Description { get; set; } = "";

        /// <summary>Relative path to a PNG icon (optional).</summary>
        public string IconPath { get; set; }

        /// <summary>Optional server-side logic (e.g. OnUsed).</summary>
        public ItemLogicBase Logic { get; set; }
    }
}
