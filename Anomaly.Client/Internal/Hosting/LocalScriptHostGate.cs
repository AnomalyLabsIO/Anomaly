using System;

namespace Anomaly.Client.Internal.Hosting;

/// <summary>Current in-process connection proof available before a player or hub is spawned.</summary>
internal static class LocalScriptHostGate
{
    internal static bool IsVerified(bool isDedicated, bool serverActive, bool clientActiveHost,
        IntPtr serverConnection, IntPtr clientConnection, IntPtr pairedClientConnection, IntPtr reciprocalServerConnection)
        => !isDedicated && serverActive && clientActiveHost &&
            serverConnection != IntPtr.Zero && clientConnection != IntPtr.Zero &&
            pairedClientConnection == clientConnection && reciprocalServerConnection == serverConnection;
}
