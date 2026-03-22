using System.Collections.Generic;
using System.Net;
using Anomaly.Shared.Auth;

namespace Anomaly.Server.Auth;

public static class AuthHandler
{
    internal static Dictionary<string, Connection> _connections = new();
    internal static Dictionary<IPEndPoint, PlayerStorage> _storedPlayers = new();

    internal static string AddConnection(ServerAuthSession session,ClientHandshake handshake)
    {
        string random;
        do
        {
            random = RandomGenerator.GetStringSecure(50);
        }
        while(_connections.ContainsKey(random));

        _connections[random] = new Connection
        {
            ServerEnv = session,
            Handshake = handshake,
        };

        return random;
    }
}
