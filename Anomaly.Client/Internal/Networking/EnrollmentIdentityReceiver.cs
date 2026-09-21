using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Networking;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.Client.Internal.Networking;

internal static class EnrollmentIdentityReceiver
{
    private static readonly TransactionalInitializer Initializer = new();

    internal static void Init()
    {
        Initializer.Initialize(
            new InitializationStep(
                () => AnomalyMessaging.MessageReceived += HandleMessage,
                () => AnomalyMessaging.MessageReceived -= HandleMessage),
            new InitializationStep(
                () => ClientEvents.Disconnected += HandleDisconnected,
                () => ClientEvents.Disconnected -= HandleDisconnected));
    }

    private static void HandleDisconnected(ClientDisconnectedEventArgs _) => ApiClient.EnrollmentId = null;

    private static void HandleMessage(IAnomalyMessage message)
    {
        if (message is EnrollmentIdentityMessage enrollment)
            ApiClient.EnrollmentId = enrollment.EnrollmentId;
    }
}
