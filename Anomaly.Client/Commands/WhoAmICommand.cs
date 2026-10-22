using System.Collections.Generic;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Commands;
using ApiClient = Anomaly.Client.Api.Features.Client;
using UnityEngine;

namespace Anomaly.Client.Commands;

internal sealed class WhoAmICommand : ICommand
{
    private static readonly IReadOnlyList<string> CommandAliases = new[] { "whoami" };

    public string Name => "whoami";
    public IReadOnlyList<string> Aliases => CommandAliases;
    public string Description => Tr.Get(TrKeys.Commands_WhoAmIDescription);

    public void Execute(CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(ApiClient.EnrollmentId))
        {
            context.Reply(Tr.Get(TrKeys.Commands_NoEnrollment));
            return;
        }

        GUIUtility.systemCopyBuffer = ApiClient.EnrollmentId;
        context.Reply(Tr.Get(TrKeys.Commands_EnrollmentCopied, ApiClient.EnrollmentId));
    }
}
