namespace Anomaly.Server.Networking;

public static class AnomalyChannel
{
    /// <summary>
    /// Reserved EncryptedChannel byte value for Anomaly custom messages.
    /// Must match the client-side value.
    /// </summary>
    public const byte ChannelId = 0xFE;
}
