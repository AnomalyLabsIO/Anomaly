using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Internal.Diagnostics;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Auth;
using UnityEngine;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.Client.Commands;

internal sealed class StatusCommand : ICommand
{
    public string Name => "status";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => Tr.Get(TrKeys.Commands_StatusDescription, "--copy");

    public void Execute(CommandContext context)
    {
        bool copy = context.Args.Count == 1 && context.Args[0] == "--copy";
        if (context.Args.Count != 0 && !copy)
        {
            context.Reply(Tr.Get(TrKeys.Commands_Usage, "anomaly.status [--copy]"));
            return;
        }
        try
        {
            string report = StatusReport.Format(typeof(Core).Assembly.GetName().Version,
                typeof(FeatureHealth).Assembly.GetName().Version,
                new Version(Il2CppGameCore.Version.Major, Il2CppGameCore.Version.Minor, Il2CppGameCore.Version.Revision),
                AuthHandler.InitializationState == AuthInitializationState.Ready &&
                    !string.IsNullOrWhiteSpace(AuthHandler.PlayerAuth.Username),
                AuthHandler.InitializationState == AuthInitializationState.Failed,
                ApiClient.IsConnected, ApiClient.IsClientReady, FeatureHealth.Snapshot().Values);
            context.Reply(report);
            if (copy)
            {
                GUIUtility.systemCopyBuffer = report;
                context.Reply(Tr.Get(TrKeys.Commands_StatusCopied));
            }
        }
        catch (Exception ex)
        {
            context.Reply(Tr.Get(TrKeys.Commands_StatusUnavailable, ex.GetType().Name));
        }
    }
}
