namespace Anomaly.Shared.Assets
{
    /// <summary>
    /// Declares how a transferred file should be interpreted by the client.
    /// Written as one byte per file entry in the manifest; the transfer pipeline
    /// itself is kind-agnostic.
    /// </summary>
    public enum AssetKind : byte
    {
        /// <summary>Opaque bytes; no typed loader. Still cacheable, caller deals with it.</summary>
        Unknown = 0,

        /// <summary>PNG / JPG. Loaded via <c>ImageConversion.LoadImage</c> on the client.</summary>
        Texture2D = 1,

        /// <summary>Unity AssetBundle (.unity3d / .bundle / .assetbundle).</summary>
        AssetBundle = 2,

        /// <summary>TTF / OTF. Loaded via <c>Font</c> constructor on the client.</summary>
        Font = 3,

        /// <summary>OGG / WAV. Loaded as a Unity <c>AudioClip</c> on the client.</summary>
        AudioClip = 4,

        // reserved: Mesh = 5, TextAsset = 6
    }
}
