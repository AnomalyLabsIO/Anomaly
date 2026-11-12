using System;

namespace Anomaly.ServerApiSmoke;

// A peer can be enumerated before channel allocation or while it is disconnecting.
// Missing instrumentation and unexpected queue failures must still fail the probe.
internal sealed class ReliableQueueSample
{
    internal int? Packets { get; }
    internal string Status { get; }

    private ReliableQueueSample(int? packets, string status)
    {
        Packets = packets;
        Status = status;
    }

    internal static ReliableQueueSample Capture(byte channelCount, Func<bool> isConnected,
        Func<Array> readChannels, Func<byte, bool, int> readQueue)
    {
        if (channelCount == 0) throw new InvalidOperationException("The transport has no configured channels.");
        if (!isConnected()) return new ReliableQueueSample(null, "peer-not-connected");
        Array channels = readChannels();
        if (channels == null) return new ReliableQueueSample(null, "channels-unavailable");
        if (channels.Rank != 1 || channels.Length != channelCount * 4)
            throw new InvalidOperationException("The peer channel array does not match the configured transport channels.");
        try
        {
            int queued = 0;
            for (byte channel = 0; channel < channelCount; channel++)
            {
                int unordered = readQueue(channel, false), ordered = readQueue(channel, true);
                if (unordered < 0 || ordered < 0) throw new InvalidOperationException("A reliable queue reported a negative count.");
                queued = checked(queued + unordered + ordered);
            }
            if (!isConnected()) return new ReliableQueueSample(null, "peer-state-changed");
            if (readChannels() == null) return new ReliableQueueSample(null, "channels-unavailable");
            return new ReliableQueueSample(queued, "sampled");
        }
        catch (NullReferenceException)
        {
            // Only suppress a confirmed peer lifetime race, not an arbitrary NRE.
            if (!isConnected()) return new ReliableQueueSample(null, "peer-state-changed");
            if (readChannels() == null) return new ReliableQueueSample(null, "channels-unavailable");
            throw;
        }
    }
}
