namespace Anomaly.Server.Api.Content
{
    public enum ContentReadiness
    {
        Unknown = 0,
        Offered = 1,
        Approved = 2,
        Transferring = 3,
        Transferred = 4,
        Declined = 5,
        Activated = 6,
        Failed = 7,
    }
}
