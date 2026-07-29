namespace Anomaly.Shared.Content
{
    /// <summary>
    /// Per-runtime materializer for catalog entries. The server (managed game types) and the
    /// client (IL2CPP) each supply their own implementation; the catalog bridge/receiver drive
    /// them through the same contract. Deliberately small for the pilot.
    /// </summary>
    public interface IContentRegistrar
    {
        /// <summary>True when every asset this entry references is locally available.</summary>
        bool AreAssetsReady(CatalogEntry entry);

        /// <summary>
        /// Materializes the entry into the live game registry (inject template, register prefab,
        /// etc.). Returns false on failure (caller reports it in the catalog ack).
        /// </summary>
        bool Register(CatalogEntry entry);

        /// <summary>
        /// Reverses <see cref="Register"/> for a previously-applied entry, returning the registry
        /// to its vanilla baseline (used by client session teardown on disconnect).
        /// </summary>
        void Unregister(CatalogEntry entry);
    }
}
