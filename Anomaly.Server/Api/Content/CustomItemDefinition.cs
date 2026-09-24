using InventorySystem;
using Anomaly.Shared.Assets;

namespace Anomaly.Server.Api.Content
{
    /// <summary>
    /// Declarative definition of a custom item. The author fills this in (resolving
    /// <see cref="DisplayName"/> through their own i18n) and passes it to
    /// <see cref="CustomItems.Register(CustomItemDefinition)"/>. Presentation assets are referenced
    /// by canonical addresses within offline-authored packs.
    /// </summary>
    public sealed class CustomItemDefinition
    {
        /// <summary>Namespaced stable key, e.g. "mymod:magic_coin". Required; at most 128 UTF-8 bytes.</summary>
        public string Key { get; set; }

        /// <summary>Vanilla item whose template is cloned for functional plumbing.</summary>
        public ItemType BaseTemplate { get; set; } = ItemType.Coin;

        /// <summary>Required display name shown in-game; at most 512 UTF-8 bytes. Resolve via your i18n before assigning.</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>Optional item description line; at most 4096 UTF-8 bytes.</summary>
        public string Description { get; set; } = "";

        /// <summary>Optional canonical Texture2D or Sprite address from an indexed pack.</summary>
        public AssetAddress? IconAddress { get; set; }

    }
}
