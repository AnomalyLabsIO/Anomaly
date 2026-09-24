using Anomaly.Server.Api.Content;
using System;
using System.Collections.Generic;
using Anomaly.Shared.Content;

namespace Anomaly.Server.Content
{
    /// <summary>Per-client consent and all-or-nothing activation state.</summary>
    internal static class ContentGrantGate
    {
        internal static event Action<uint> Activated;
        internal static event Action<uint> Revoked;
        internal static bool PublishActivated(uint netId)
        {
            if (GetReadiness(netId) != ContentReadiness.Activated) return false;
            bool success = true;
            foreach (Delegate handler in Activated?.GetInvocationList() ?? Array.Empty<Delegate>())
                try { ((Action<uint>)handler)(netId); } catch { success = false; }
            return success;
        }
        private sealed class State
        {
            public ContentReadiness Readiness;
            public byte[] CatalogSha256;
            public byte[] ManifestSha256;
        }

        private static readonly Dictionary<uint, State> ByNetId = new Dictionary<uint, State>();

        internal static void RecordOffer(uint netId, ContentCatalog catalog)
        {
            ByNetId[netId] = new State
            {
                Readiness = ContentReadiness.Offered,
                CatalogSha256 = (byte[])catalog.CatalogSha256.Clone(),
                ManifestSha256 = (byte[])catalog.ManifestSha256.Clone(),
            };
        }

        internal static bool TryRecordDecision(
            uint netId,
            byte[] catalogSha256,
            byte[] manifestSha256,
            bool approved)
        {
            if (!TryMatch(netId, catalogSha256, manifestSha256, out State state) ||
                state.Readiness != ContentReadiness.Offered)
                return false;
            state.Readiness = approved ? ContentReadiness.Approved : ContentReadiness.Declined;
            return true;
        }

        internal static bool TryBeginTransfer(uint netId)
        {
            if (!ByNetId.TryGetValue(netId, out State state) ||
                state.Readiness != ContentReadiness.Approved)
                return false;
            state.Readiness = ContentReadiness.Transferring;
            return true;
        }

        internal static bool TryCompleteTransfer(uint netId)
        {
            if (!ByNetId.TryGetValue(netId, out State state) ||
                state.Readiness != ContentReadiness.Transferring)
                return false;
            state.Readiness = ContentReadiness.Transferred;
            return true;
        }

        internal static bool TryRecordActivation(
            uint netId,
            byte[] catalogSha256,
            byte[] manifestSha256,
            bool activated)
        {
            if (!TryMatch(netId, catalogSha256, manifestSha256, out State state))
                return false;

            if (activated)
            {
                if (state.Readiness != ContentReadiness.Transferred)
                    return false;
                state.Readiness = ContentReadiness.Activated;
                return true;
            }

            if (state.Readiness != ContentReadiness.Approved &&
                state.Readiness != ContentReadiness.Transferring &&
                state.Readiness != ContentReadiness.Transferred)
                return false;
            state.Readiness = ContentReadiness.Failed;
            return true;
        }

        internal static ContentReadiness GetReadiness(uint netId) =>
            ByNetId.TryGetValue(netId, out State state) ? state.Readiness : ContentReadiness.Unknown;

        internal static bool CanGrant(uint netId, string key) =>
            ByNetId.TryGetValue(netId, out State state) &&
            state.Readiness == ContentReadiness.Activated &&
            ContentRegistry.TryGetId(key, out _);

        internal static void Remove(uint netId)
        {
            ByNetId.Remove(netId);
            foreach (Delegate handler in Revoked?.GetInvocationList() ?? Array.Empty<Delegate>())
                try { ((Action<uint>)handler)(netId); } catch { }
        }
        internal static void Reset()
        {
            foreach (uint netId in new List<uint>(ByNetId.Keys)) Remove(netId);
        }

        private static bool TryMatch(
            uint netId,
            byte[] catalogSha256,
            byte[] manifestSha256,
            out State state)
        {
            return ByNetId.TryGetValue(netId, out state) &&
                   CatalogBuilder.HashesEqual(state.CatalogSha256, catalogSha256) &&
                   CatalogBuilder.HashesEqual(state.ManifestSha256, manifestSha256);
        }
    }
}
