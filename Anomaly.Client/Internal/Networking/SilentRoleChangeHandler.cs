using Anomaly.Client.Internal;
using Anomaly.Client.Api.Internal;
using Anomaly.Shared.Networking;
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
        private static readonly TransactionalInitializer Initializer = new();

        internal static void Register()
        {
            Initializer.Initialize(new InitializationStep(
                () => AnomalyMessaging.MessageReceived += HandleMessage,
                () => AnomalyMessaging.MessageReceived -= HandleMessage));
        }

        private static void HandleMessage(IAnomalyMessage message)
        {
            if (message is SilentRoleChangeMessage)
                SilentRoleChange.IsPending = true;
        }
    }
}
