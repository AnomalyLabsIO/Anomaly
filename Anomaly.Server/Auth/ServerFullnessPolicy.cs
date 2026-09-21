using System;

namespace Anomaly.Server.Auth
{
    /// <summary>
    /// Keeps the authenticated identity and the exact preauthentication flags
    /// together when invoking the game's user-aware fullness/reserved-slot path.
    /// </summary>
    internal static class ServerFullnessPolicy
    {
        internal static bool IsFull<TFlags>(
            string authenticatedUserId,
            TFlags preauthenticationFlags,
            Func<string, TFlags, bool> evaluate)
        {
            if (string.IsNullOrWhiteSpace(authenticatedUserId))
                throw new ArgumentException("An authenticated user ID is required.", nameof(authenticatedUserId));
            if (evaluate == null)
                throw new ArgumentNullException(nameof(evaluate));

            return evaluate(authenticatedUserId, preauthenticationFlags);
        }
    }
}
