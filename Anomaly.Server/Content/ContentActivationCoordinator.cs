using Anomaly.Server.Api.Content;
using System;

namespace Anomaly.Server.Content
{
    /// <summary>
    /// Commits one matching activation and publishes readiness only for the
    /// transition that won the grant-gate state change.
    /// </summary>
    internal static class ContentActivationCoordinator
    {
        internal static bool TryCommit(
            uint netId,
            byte[] catalogSha256,
            byte[] manifestSha256,
            bool activated,
            Func<bool> publishReady,
            out bool publicationSucceeded)
        {
            if (publishReady == null)
                throw new ArgumentNullException(nameof(publishReady));

            publicationSucceeded = true;
            if (!ContentGrantGate.TryRecordActivation(
                    netId,
                    catalogSha256,
                    manifestSha256,
                    activated))
                return false;

            if (activated)
                publicationSucceeded = publishReady() && ContentGrantGate.PublishActivated(netId);
            return true;
        }
    }
}
