using System;

namespace Anomaly.Shared.Networking
{
    /// <summary>
    /// Pure boundary policy for checking an encoded Anomaly message against the
    /// packet limit reported by the active transport for its selected channel.
    /// </summary>
    internal static class TransportPacketPolicy
    {
        internal const int FramingReserveBytes = 64;

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
