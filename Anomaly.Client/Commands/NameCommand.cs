using System.Collections.Generic;
using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Commands;

/// <summary>
/// Built-in "anomaly.name" command — prints the local player's nickname, or a
/// not-connected message when invoked from the main menu. Doubles as a smoke
/// test that the command pipeline is reachable at runtime.
/// </summary>
internal sealed class NameCommand : ICommand
{
    private static readonly IReadOnlyList<string> _aliases = new[] { "name" };

    public string Name => "name";

    public IReadOnlyList<string> Aliases => _aliases;

    public string Description => "Prints the local player's nickname.";

    public void Execute(CommandContext context)
    {
        var hub = LocalPlayer.Instance?.ReferenceHub;
        if (hub == null)
        {
            context.Reply("(not connected)");
            return;
        }

        string nick = hub.nicknameSync?.MyNick;
        context.Reply(string.IsNullOrEmpty(nick) ? "(no nickname set)" : nick);
    }
}
