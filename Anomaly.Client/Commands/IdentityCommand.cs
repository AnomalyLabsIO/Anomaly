using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Commands;
using Anomaly.Client.Auth;

namespace Anomaly.Client.Commands;

/// <summary>Explicit local maintenance command for old-key-authorized identity rotation.</summary>
internal sealed class IdentityCommand : ICommand
{
    private static readonly IReadOnlyList<string> NoAliases = Array.Empty<string>();

    public string Name => "rotate_identity";

    public IReadOnlyList<string> Aliases => NoAliases;

    public string Description => "Rotate the client identity while preserving signed enrollment lineage.";

    public void Execute(CommandContext context)
    {
        if (!string.Equals(context.ArgOr(0), "confirm", StringComparison.Ordinal))
        {
            context.Reply(
                "Usage: anomaly.rotate_identity confirm — disconnect first. "
                + "The previous key signs a bounded public lineage proof; losing user.json instead creates a fresh enrollment.");
            return;
        }

        if (!AuthHandler.TryRotateClientIdentity(out var error))
        {
            context.Reply("Client identity rotation failed: " + error);
            return;
        }

        context.Reply(
            "Client identity rotated and saved atomically. Future connections present the old-key-signed lineage "
            + "so servers retain the existing local enrollment.");
    }
}
