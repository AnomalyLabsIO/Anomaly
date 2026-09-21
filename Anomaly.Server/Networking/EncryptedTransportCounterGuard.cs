namespace Anomaly.Server.Networking;

internal static class EncryptedTransportCounterGuard
{
    // Keep uint.MaxValue unused so no peer ever has to guess whether the next
    // counter wrapped to zero under the same authenticated session.
    public static bool TryAdvance(uint current, out uint next)
    {
        if (current >= uint.MaxValue - 1)
        {
            next = 0;
            return false;
        }
        next = current + 1;
        return true;
    }

    public static bool CanAccept(uint current, uint candidate)
        => candidate != 0 && candidate != uint.MaxValue && candidate > current;
}
