using System;
using Anomaly.Server.Auth;
using CommandSystem;

namespace Anomaly.Server.Commands;

/// <summary>
/// Local server-console control for inspecting and deliberately rotating the
/// protocol-v2 signing identity. Remote-admin command handlers are intentionally
/// not registered for this security-sensitive operation.
/// </summary>
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class ServerIdentityCommand : ICommand
{
    public string Command => "anomalyidentity";

    public string[] Aliases => Array.Empty<string>();

    public string Description => "Shows or rotates the persistent Anomaly server identity.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        string operation = arguments.Count == 0
            ? "status"
            : arguments.Array[arguments.Offset];

        if (string.Equals(operation, "status", StringComparison.OrdinalIgnoreCase))
        {
            var record = ServerIdentityStore.GetActiveRotationRecord();
            response = "Anomaly server identity fingerprint: " + ServerIdentityStore.Fingerprint;
            if (record != null)
            {
                response += $"; signed rotation sequence {record.Sequence} remains valid until "
                    + DateTimeOffset.FromUnixTimeSeconds(record.ExpiresUnixSeconds).UtcDateTime.ToString("O") + ".";
            }
            return true;
        }

        if (!string.Equals(operation, "rotate", StringComparison.OrdinalIgnoreCase))
        {
            response = "Usage: anomalyidentity status | anomalyidentity rotate";
            return false;
        }

        if (!ServerIdentityStore.TryRotate(out var rotation, out var error))
        {
            response = "Failed to rotate the Anomaly server identity: " + error;
            return false;
        }

        response = "Rotated the Anomaly server identity with an old-key-signed record. "
            + "New fingerprint: " + ServerIdentityStore.Fingerprint
            + $"; sequence: {rotation.Sequence}; automatic client migration expires: "
            + DateTimeOffset.FromUnixTimeSeconds(rotation.ExpiresUnixSeconds).UtcDateTime.ToString("O") + ".";
        return true;
    }
}
