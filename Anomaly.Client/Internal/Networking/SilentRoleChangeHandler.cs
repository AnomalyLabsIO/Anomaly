using Anomaly.Client.Internal;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Client.Api.Networking
{
    /// <summary>
    /// Client-side subscription wiring for <see cref="SilentRoleChangeMessage"/>.
    /// The message class itself lives in Anomaly.Shared; this class carries the
    /// side-effect that flips <see cref="SilentRoleChange.IsPending"/> when one arrives.
    /// </summary>
    internal static class SilentRoleChangeHandler
    {
        internal static void Register()
        {
            AnomalyMessaging.MessageReceived += msg =>
            {
                if (msg is SilentRoleChangeMessage)
                    SilentRoleChange.IsPending = true;
            };
        }
    }
}
