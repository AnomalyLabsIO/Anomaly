using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Commands;
using Anomaly.Client.Auth;

namespace Anomaly.Client.Commands;

/// <summary>Explicit local maintenance command for old-key-authorized identity rotation.</summary>
internal sealed class IdentityCommand : ICommand
{
    private static readonly IReadOnlyList<string> NoAliases = Array.Empty<string>();

    public string Name => "rotate_identity";

    public IReadOnlyList<string> Aliases => NoAliases;

    public string Description => Tr.Get(TrKeys.Commands_IdentityDescription);

    public void Execute(CommandContext context)
    {
        if (!string.Equals(context.ArgOr(0), "confirm", StringComparison.Ordinal))
        {
            context.Reply(
                Tr.Get(TrKeys.Commands_IdentityUsage, "anomaly.rotate_identity confirm", "user.json"));
            return;
        }

        if (!AuthHandler.TryRotateClientIdentity(out var error))
        {
            context.Reply(Tr.Get(TrKeys.Commands_IdentityFailed, error));
            return;
        }

        context.Reply(
            Tr.Get(TrKeys.Commands_IdentityRotated));
    }
}
