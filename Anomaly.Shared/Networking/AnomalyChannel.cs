namespace Anomaly.Shared.Networking
{
    public static class AnomalyChannel
    {
        /// <summary>
        /// Reserved EncryptedChannel byte value for Anomaly custom messages.
        /// This value must not collide with any game EncryptedChannel enum value.
        /// </summary>
        public const byte ChannelId = 0xFE;
    }
}
