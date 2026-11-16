using System;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Server.FileTransfer;

/// <summary>Operator-provided upload allowance, with gameplay reserve and conservative frame accounting.</summary>
internal sealed class BulkTransferBudget
{
    internal const int DefaultClientBytesPerSecond = 8 * 1024 * 1024;
    internal const int DefaultUploadBytesPerSecond = 16 * 1024 * 1024;
    internal const int DefaultHeadroomPercent = 20;
    internal static readonly BulkTransferBudget Default = new BulkTransferBudget(
        DefaultClientBytesPerSecond, DefaultUploadBytesPerSecond, DefaultHeadroomPercent);

    internal BulkTransferBudget(int clientBytesPerSecond, int uploadBytesPerSecond, int headroomPercent)
    {
        if (clientBytesPerSecond < 64 * 1024 || clientBytesPerSecond > 16 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(clientBytesPerSecond), "Bulk client upload allowance must be 64 KiB/s–16 MiB/s.");
        if (uploadBytesPerSecond < 64 * 1024 || uploadBytesPerSecond > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(uploadBytesPerSecond), "Bulk aggregate upload allowance must be 64 KiB/s–64 MiB/s.");
        if (headroomPercent < 10 || headroomPercent > 90)
            throw new ArgumentOutOfRangeException(nameof(headroomPercent), "Bulk gameplay headroom must be 10–90 percent.");
        GlobalBytesPerSecond = (long)uploadBytesPerSecond * (100 - headroomPercent) / 100;
        ClientBytesPerSecond = Math.Min(GlobalBytesPerSecond,
            (long)clientBytesPerSecond * (100 - headroomPercent) / 100);
    }

    internal long ClientBytesPerSecond { get; }
    internal long GlobalBytesPerSecond { get; }

    // Includes encrypted/Mirror/fragment/IP framing, ACKs, and a retransmission margin.
    // This is conservative admission accounting, not a measurement of actual wire traffic.
    internal static int Charge(int payloadBytes) => checked(payloadBytes + (payloadBytes + 3) / 4 + 256);
    internal static int PayloadAllowance(double accountedBytes) => accountedBytes <= 256 ? 0 :
        (int)Math.Min(int.MaxValue, (accountedBytes - 256) * 4 / 5);
    internal static double TransferCharge(long payloadBytes) => payloadBytes * 1.25 +
        Math.Ceiling((double)payloadBytes / FileChunkMessage.MaxChunkBytes) * 256;
    internal static double Burst(long rate) => Math.Max(Charge(FileChunkMessage.MaxChunkBytes),
        Math.Min(BulkTransferPump.MaximumBytesPerUpdate, rate / 10d));
}
