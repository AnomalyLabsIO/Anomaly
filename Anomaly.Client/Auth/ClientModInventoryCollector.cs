namespace Anomaly.Client.Auth;

internal static class ClientModInventoryCollector
{
    // Read Bootstrap's detached lifetime ledger, never a mutable current registry.
    internal static ClientAdmissionSnapshot Capture() => ClientAdmissionGuard.Capture();
}
