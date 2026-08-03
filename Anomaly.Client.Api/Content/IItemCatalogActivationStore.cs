namespace Anomaly.Client.Api.Content;

public interface IItemCatalogActivationStore
{
    ItemCatalogActivationSnapshot Load();
    void SaveAtomically(ItemCatalogActivationSnapshot snapshot);
}
