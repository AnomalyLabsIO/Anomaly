using System.Collections.Generic;
using Anomaly.Client.Api.Commands;
using ApiClient = Anomaly.Client.Api.Features.Client;
using UnityEngine;

namespace Anomaly.Client.Commands;

internal sealed class WhoAmICommand : ICommand
{
    private static readonly IReadOnlyList<string> CommandAliases = new[] { "whoami" };

    public string Name => "whoami";
    public IReadOnlyList<string> Aliases => CommandAliases;
    public string Description => "Shows and copies this server's local Anomaly enrollment id.";

    public void Execute(CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(ApiClient.EnrollmentId))
        {
            context.Reply("No authenticated server enrollment is active.");
            return;
        }

        GUIUtility.systemCopyBuffer = ApiClient.EnrollmentId;
        context.Reply(ApiClient.EnrollmentId + " (copied to clipboard)");
    }
}
