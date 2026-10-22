using System;
using Anomaly.Shared.Auth;

namespace Anomaly.Shared.Networking
{
    /// <summary>
    /// Pure boundary policy for checking an encoded Anomaly message against the
    /// packet limit reported by the active transport for its selected channel.
    /// </summary>
    internal static class TransportPacketPolicy
    {
        internal const int FramingReserveBytes = 64;
        internal const int AnomalyEnvelopeBytes = 1 + sizeof(uint) + sizeof(uint);

        internal static int GetMaximumPacketBytes(int singlePacketBytes, bool allowsFragmentation)
        {
            if (singlePacketBytes <= 0)
                return 0;
            if (!allowsFragmentation)
                return singlePacketBytes;

            // LiteNetLib reliable fragments add six bytes after the normal packet header.
            long maximum = (long)(singlePacketBytes - 6) * ushort.MaxValue;
            return maximum <= 0 ? 0 : (int)Math.Min(maximum, int.MaxValue);
        }

        internal static bool TryGetPayloadBudget(
            int maximumPacketBytes,
            int singlePacketBytes,
            int messageMaximumPayloadBytes,
            bool encrypted,
            out MessagePayloadBudget budget)
        {
            budget = default;
            if (maximumPacketBytes <= 0 || singlePacketBytes <= 0 ||
                singlePacketBytes > maximumPacketBytes || messageMaximumPayloadBytes < 0 ||
                messageMaximumPayloadBytes > AnomalyChannel.MaxEnvelopeBytes)
                return false;

            int overhead = AnomalyEnvelopeBytes + FramingReserveBytes +
                           (encrypted ? AuthTransportCrypto.EnvelopeOverhead : 0);
            if (singlePacketBytes < overhead)
                return false;

            int maximum = Math.Min(messageMaximumPayloadBytes,
                Math.Min(AnomalyChannel.MaxEnvelopeBytes - AnomalyEnvelopeBytes,
                    maximumPacketBytes - overhead));
            budget = new MessagePayloadBudget(maximum, Math.Min(maximum, singlePacketBytes - overhead));
            return true;
        }

        internal static bool FitsReportedChannel(
            MessageChannel channel,
            int encodedBytes,
            Func<int, int> getMaximumPacketSize,
            out int reportedMaximumPacketBytes)
        {
            if (getMaximumPacketSize == null)
                throw new ArgumentNullException(nameof(getMaximumPacketSize));

            reportedMaximumPacketBytes = getMaximumPacketSize((int)channel);
            return Fits(reportedMaximumPacketBytes, encodedBytes);
        }

        internal static bool Fits(int reportedMaximumPacketBytes, int encodedBytes)
        {
            if (reportedMaximumPacketBytes <= 0 || encodedBytes < 0)
                return false;

            try
            {
                return checked(encodedBytes + FramingReserveBytes) <= reportedMaximumPacketBytes;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }
}
