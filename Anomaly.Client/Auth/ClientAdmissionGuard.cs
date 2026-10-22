using System.Runtime.CompilerServices;
using Anomaly.Bootstrap;
using Anomaly.Shared.Auth;

namespace Anomaly.Client.Auth;

internal sealed class ClientAdmissionSnapshot
{
    internal ClientModInventory Inventory { get; }
    internal ClientIntegrityAudit Audit { get; }
    internal long GuardGeneration { get; }
    internal long InventoryGeneration { get; }

    internal ClientAdmissionSnapshot(ClientModInventory inventory, ClientIntegrityAudit audit,
        long guardGeneration, long inventoryGeneration)
    {
        Inventory = inventory;
        Audit = audit;
        GuardGeneration = guardGeneration;
        InventoryGeneration = inventoryGeneration;
    }
}

internal sealed class ClientIntegrityException : Exception
{
    internal ClientIntegrityException(string message) : base(message) { }
}

/// <summary>Converts Bootstrap's process-lifetime evidence into one frozen admission claim.</summary>
internal static class ClientAdmissionGuard
{
    // Keep Bootstrap type resolution behind a non-inlined call so the caller can
    // catch an absent or incompatible plugin before any authentication is sent.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void EnsureAvailable()
    {
        // Resolve newly loaded targets only on the initialized support thread;
        // a worker never JITs or initializes IL2CPP types during a send check.
        ProtectedMethodCatalog.RefreshNativeTargetsIfReady();
        if (Bootstrap.Core.State != StartupState.Ready || !RuntimeGuard.Verify())
            throw new ClientIntegrityException("The Bootstrap authentication guard is unavailable. Restart the game.");
        // Core registered its exact required patch tuples before auth initialization;
        // the shared Bootstrap checkpoint verifies those cached descriptors too.
        var guard = RuntimeGuard.Snapshot;
        if (!guard.Healthy)
            throw new ClientIntegrityException("Required Bootstrap protection failed. Restart the game.");
        if (guard.MutationInProgress != 0)
            throw new ClientIntegrityException("An authentication-path modification is in progress. Reconnect after it finishes.");
        if (!guard.AllowExternalModifications && (guard.Flags & 15) != 0)
            throw new ClientIntegrityException(
                "Authentication-path modifications or incomplete monitoring were detected. " +
                "Remove the affected mod and restart, or explicitly enable external modifications for a server that accepts the risk.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ClientAdmissionSnapshot Capture()
    {
        EnsureAvailable();
        var source = AdmissionInventoryLedger.CaptureAdmission();
        var guard = RuntimeGuard.Snapshot;
        var inventory = new ClientModInventory(source.Entries
            .Select(entry => new ClientModEntry(entry.Id, entry.Version)));
        inventory.Encode();
        var result = new ClientAdmissionSnapshot(inventory,
            new ClientIntegrityAudit((ClientIntegrityAuditFlags)guard.Flags, guard.AllowExternalModifications),
            guard.Generation, source.Generation);
        EnsureCurrent(result);
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void EnsureCurrent(ClientAdmissionSnapshot claim, bool reconcile = false)
    {
        if (claim == null)
            throw new ClientIntegrityException("Authentication has no frozen Bootstrap audit.");
        if (reconcile)
        {
            EnsureAvailable();
            AdmissionInventoryLedger.CaptureAdmission();
        }

        var guard = RuntimeGuard.Snapshot;
        if (!guard.Healthy || guard.MutationInProgress != 0 || Bootstrap.Core.State != StartupState.Ready ||
            (!ProtectedMethodCatalog.NativeCoverageReady &&
             (claim.Audit.Flags & ClientIntegrityAuditFlags.CoverageIncomplete) == 0) ||
            guard.Generation != claim.GuardGeneration ||
            !AdmissionInventoryLedger.VerifyGeneration(claim.InventoryGeneration))
            throw new ClientIntegrityException(
                "The loaded mod inventory or authentication-path audit changed. Reconnect with a fresh handshake.");
        if (!guard.AllowExternalModifications && (guard.Flags & 15) != 0)
            throw new ClientIntegrityException("External authentication modifications are disabled for this client.");
    }
}
