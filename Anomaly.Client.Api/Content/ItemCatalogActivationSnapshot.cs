namespace Anomaly.Client.Api.Content;

public sealed class ItemCatalogActivationSnapshot
{
    public ItemCatalogActivationState Active { get; set; }
    public ItemCatalogActivationState Previous { get; set; }
}
