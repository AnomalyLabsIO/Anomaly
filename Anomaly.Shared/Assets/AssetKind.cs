namespace Anomaly.Shared.Assets
{
    /// <summary>
    /// The type of an asset at a catalog address. Transfer containers have a separate kind.
    /// </summary>
    public enum AssetKind : byte
    {
        /// <summary>Reserved; never a valid catalog type.</summary>
        Unknown = 0,

        /// <summary>A bundle Texture2D.</summary>
        Texture2D = 1,

        /// <summary>A bundle Sprite and its validated texture dependency.</summary>
        Sprite = 2,

        /// <summary>A static bundle Mesh.</summary>
        Mesh = 3,

        /// <summary>A bundle AudioClip.</summary>
        AudioClip = 4,

        /// <summary>A bounded, data-only hierarchy constructed by Anomaly.</summary>
        DecorativeVisual = 5,
    }
}
