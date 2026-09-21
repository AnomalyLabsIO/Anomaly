namespace Anomaly.Shared.Assets
{
    /// <summary>
    /// Declares how a transferred file should be interpreted by the client.
    /// Written as one byte per file entry in the manifest; the transfer pipeline
    /// itself is kind-agnostic.
    /// </summary>
    public enum AssetKind : byte
    {
        /// <summary>Reserved. Remote v1 manifests reject this value.</summary>
        Unknown = 0,

        /// <summary>PNG image loaded as a Unity texture.</summary>
        Texture2D = 1,

        /// <summary>OGG audio loaded as a Unity <c>AudioClip</c> on the client.</summary>
        AudioClip = 4,
    }
}
