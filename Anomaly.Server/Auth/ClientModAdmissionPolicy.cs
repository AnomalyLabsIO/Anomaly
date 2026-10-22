using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Anomaly.Server.Api.Auth;
using Anomaly.Server.Utils;
using Anomaly.Shared.Auth;

namespace Anomaly.Server.Auth;

/// <summary>Evaluates detached owner and companion requirements on the preauth worker thread.</summary>
internal static class ClientModAdmissionPolicy
{
    private sealed class OwnerPolicy
    {
        internal static readonly OwnerPolicy Empty = new OwnerPolicy(
            false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Version(0, 0, 0, 0), "0.0.0", false);

        internal OwnerPolicy(bool blockAnyAdditional, HashSet<string> blockedIds,
            Version coreVersion, string coreVersionText, bool allowRiskyClientModifications)
        {
            BlockAnyAdditional = blockAnyAdditional;
            BlockedIds = blockedIds;
            CoreVersion = coreVersion;
            CoreVersionText = coreVersionText;
            AllowRiskyClientModifications = allowRiskyClientModifications;
        }

        internal bool BlockAnyAdditional { get; }
        internal HashSet<string> BlockedIds { get; }
        internal Version CoreVersion { get; }
        internal string CoreVersionText { get; }
        internal bool AllowRiskyClientModifications { get; }
    }

    private static OwnerPolicy _ownerPolicy = OwnerPolicy.Empty;
    private static readonly object WarningSync = new object();
    private static readonly HashSet<string> WarnedConflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal static void Configure(Config config, Version coreVersion)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (coreVersion == null)
            throw new ArgumentNullException(nameof(coreVersion));
        if (config.BlockedClientModIds == null)
            throw new ArgumentException("BlockedClientModIds must be a list (empty is allowed).", nameof(config));

        var blockedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in config.BlockedClientModIds)
        {
            if (!ClientModRequirements.IsWireValidId(id))
                throw new ArgumentException("BlockedClientModIds contains an invalid or oversized mod ID: " + id,
                    nameof(config));
            blockedIds.Add(id);
        }

        string coreVersionText = coreVersion.Build < 0
            ? coreVersion.ToString(2)
            : coreVersion.Revision < 0 ? coreVersion.ToString(3) : coreVersion.ToString(4);
        var normalizedCoreVersion = new Version(
            coreVersion.Major, coreVersion.Minor, Math.Max(0, coreVersion.Build),
            Math.Max(0, coreVersion.Revision));
        Volatile.Write(ref _ownerPolicy,
            new OwnerPolicy(config.BlockAnyAdditionalClientMods, blockedIds,
                normalizedCoreVersion, coreVersionText, config.AllowRiskyClientModifications));
        lock (WarningSync)
            WarnedConflicts.Clear();
        WarnIfBlocked(ClientModInventory.CoreId);
        foreach (ClientModRequirement requirement in ClientModRequirements.Snapshot())
            WarnIfBlocked(requirement.Id);
    }

    internal static void Reset()
    {
        Volatile.Write(ref _ownerPolicy, OwnerPolicy.Empty);
        lock (WarningSync)
            WarnedConflicts.Clear();
    }

    internal static void WarnIfBlocked(string id)
    {
        if (!Volatile.Read(ref _ownerPolicy).BlockedIds.Contains(id))
            return;
        lock (WarningSync)
        {
            if (!WarnedConflicts.Add(id))
                return;
        }
        string requiredBy = StringComparer.OrdinalIgnoreCase.Equals(id, ClientModInventory.CoreId)
            ? "Anomaly"
            : "a server plugin";
        OffThreadLogger.Warn(
            $"Client mod policy conflict: '{id}' is required by {requiredBy} and explicitly blocked. " +
            "The explicit block takes precedence.");
    }

    internal static bool AllowsIntegrity(ClientIntegrityAudit audit)
    {
        if (audit == null)
            return false;
        return !audit.IsRisky || Volatile.Read(ref _ownerPolicy).AllowRiskyClientModifications;
    }

    internal static IReadOnlyList<ModAdmissionIssue> Evaluate(ClientModInventory inventory)
    {
        if (inventory == null)
            throw new ArgumentNullException(nameof(inventory));

        OwnerPolicy policy = Volatile.Read(ref _ownerPolicy);
        ClientModRequirement[] requirements = ClientModRequirements.Snapshot();
        var reported = new Dictionary<string, ClientModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (ClientModEntry entry in inventory.Entries)
        {
            if (reported.ContainsKey(entry.Id))
                throw new InvalidDataException("Client mod inventory contains case-colliding IDs.");
            reported.Add(entry.Id, entry);
        }
        var issues = new List<ModAdmissionIssue>();
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Explicit owner blocks win even when a companion also requires that ID.
        foreach (ClientModEntry entry in inventory.Entries)
        {
            if (policy.BlockedIds.Contains(entry.Id))
            {
                blocked.Add(entry.Id);
                issues.Add(new ModAdmissionIssue("blocked", entry.Id, null, entry.Version));
            }
        }

        if (!blocked.Contains(ClientModInventory.CoreId))
        {
            if (!reported.TryGetValue(ClientModInventory.CoreId, out var core))
                issues.Add(new ModAdmissionIssue("missing", ClientModInventory.CoreId,
                    policy.CoreVersionText, null));
            else if (!ClientModRequirements.TryParseStableVersion(core.Version, out var version) ||
                     version != policy.CoreVersion)
                issues.Add(new ModAdmissionIssue("wrong_version", ClientModInventory.CoreId,
                    policy.CoreVersionText, core.Version));
        }

        var requiredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ClientModRequirement requirement in requirements
                     .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Id, StringComparer.Ordinal)
                     .ThenBy(item => item.Minimum)
                     .ThenBy(item => item.Maximum))
        {
            requiredIds.Add(requirement.Id);
            if (blocked.Contains(requirement.Id))
                continue;

            if (!reported.TryGetValue(requirement.Id, out var present))
            {
                issues.Add(new ModAdmissionIssue("missing", requirement.Id,
                    requirement.Description, null));
            }
            else if (!ClientModRequirements.TryParseStableVersion(present.Version, out var version) ||
                     version < requirement.Minimum ||
                     (requirement.Maximum != null && version >= requirement.Maximum))
            {
                issues.Add(new ModAdmissionIssue("wrong_version", requirement.Id,
                    requirement.Description, present.Version));
            }
        }

        if (policy.BlockAnyAdditional)
        {
            foreach (ClientModEntry entry in inventory.Entries)
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(entry.Id, ClientModInventory.CoreId) &&
                    !requiredIds.Contains(entry.Id) && !blocked.Contains(entry.Id))
                    issues.Add(new ModAdmissionIssue("unexpected", entry.Id, null, entry.Version));
            }
        }

        return issues;
    }
}
