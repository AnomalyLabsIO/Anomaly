using System.Collections;
using Il2Cpp;
using Il2CppCommandSystem;
using Il2CppCommandSystem.Commands.Console;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMirror;
using MelonLoader;

namespace Anomaly.Client.Internal.Hosting;

internal static class LocalHostingRuntime
{
    private static readonly LocalHostGrantCoordinator Grants = new();
    private static bool _starting;

    // Return true only when the original warning flow should handle the request.
    internal static bool StartLocalHost()
    {
        bool startAttempted = false;
        try
        {
            if (ServerStatic.IsDedicated) return true;

            var manager = CustomNetworkManager.TypedSingleton;
            if (manager == null)
            {
                MelonLogger.Warning("[LocalHosting] Network manager is unavailable; using the normal hosting flow.");
                return true;
            }

            if (_starting || NetworkServer.active || NetworkClient.active ||
                manager.mode != NetworkManagerMode.Offline)
                return false;

            _starting = true;
            startAttempted = true;
            // This is the stock Create Anyway action. Passing false to
            // StartNondedicated only changes the warning, and never starts a host.
            Anomaly.Client.Auth.AuthHandler.PrepareForLocalHostStart();
            manager.CreateMatch();
            return false;
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"[LocalHosting] Could not start the local game: {exception}");
            // CreateMatch may already have loaded configs or started networking.
            // Never fall through and offer a second startup after that call.
            return !startAttempted;
        }
        finally
        {
            if (startAttempted) _starting = false;
        }
    }

    internal static void ScheduleAdminGrant(ServerRoles roles)
    {
        try
        {
            LocalHostIdentitySnapshot identity = CaptureIdentity(roles);
            if (!Grants.TrySchedule(identity, out LocalHostGrantTicket ticket)) return;
            MelonCoroutines.Start(GrantOnNextFrame(roles, ticket));
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"[LocalHosting] Could not schedule local-host admin permissions: {exception}");
        }
    }

    private static IEnumerator GrantOnNextFrame(ServerRoles roles, LocalHostGrantTicket ticket)
    {
        // ServerRoles.Start has initialized its hub. Wait for the other player
        // components' Start methods, including QueryProcessor's RA handlers.
        yield return null;

        try
        {
            if (!Grants.TryBeginGrant(ticket, CaptureIdentity(roles))) yield break;

            var hub = roles._hub;
            var query = hub.queryProcessor;
            if (ServerStatic.PermissionsHandler == null || query == null || query._hub == null ||
                query._hub.Pointer != hub.Pointer || !query.TryGetSender(out var sender) || sender == null)
            {
                MelonLogger.Warning("[LocalHosting] Local admin components are not ready; adminme remains available.");
                yield break;
            }

            // ArraySegment<T> is a reference wrapper in IL2CPP: default/null
            // cannot be passed to Execute even though adminme ignores arguments.
            var arguments = new Il2CppSystem.ArraySegment<string>(new Il2CppStringArray(0));
            if (new AdminMeCommand().Execute(arguments, sender.Cast<ICommandSender>(), out string response))
                MelonLogger.Msg("[LocalHosting] Granted administrator permissions to the local host.");
            else
                MelonLogger.Warning($"[LocalHosting] Automatic admin grant was rejected: {response}");
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"[LocalHosting] Automatic local-host admin grant failed: {exception}");
        }
    }

    private static LocalHostIdentitySnapshot CaptureIdentity(ServerRoles roles)
    {
        if (roles == null || !NetworkServer.active || !NetworkClient.activeHost || ServerStatic.IsDedicated)
            return default;

        var hub = roles._hub;
        if (hub == null || !hub.isLocalPlayer ||
            !ReferenceHub.TryGetLocalHub(out var localHub) || localHub == null ||
            !ReferenceHub.TryGetHostHub(out var hostHub) || hostHub == null)
            return default;

        var identity = hub.netIdentity;
        var localPlayer = NetworkClient.localPlayer;
        var connection = hub.connectionToClient;
        var serverConnection = NetworkServer.localConnection;
        if (identity == null || localPlayer == null || connection == null || serverConnection == null)
            return default;

        var serverIdentity = serverConnection.identity;
        var pairedClient = serverConnection.connectionToServer;
        var clientConnection = NetworkClient.connection;
        if (serverIdentity == null || pairedClient == null || clientConnection == null ||
            pairedClient.connectionToClient == null)
            return default;

        // Native pointer equality handles separate managed wrappers of the same
        // IL2CPP object. The actual in-process connection pair, not an IP/user ID,
        // proves that this player owns the host even if LAN hosting is added.
        return new LocalHostIdentitySnapshot
        {
            ServerActive = NetworkServer.active,
            HostClientActive = NetworkClient.activeHost,
            IsDedicated = ServerStatic.IsDedicated,
            IsLocalPlayer = hub.isLocalPlayer,
            Hub = hub.Pointer,
            LocalHub = localHub.Pointer,
            HostHub = hostHub.Pointer,
            Identity = identity.Pointer,
            LocalPlayerIdentity = localPlayer.Pointer,
            ConnectionToClient = connection.Pointer,
            ServerLocalConnection = serverConnection.Pointer,
            ServerConnectionIdentity = serverIdentity.Pointer,
            ConnectionToServer = pairedClient.Pointer,
            ClientConnection = clientConnection.Pointer,
            ReciprocalConnectionToClient = pairedClient.connectionToClient.Pointer
        };
    }

    internal static void RemovePlayer(ReferenceHub hub)
    {
        if (hub != null) Grants.Remove(hub.Pointer);
    }

    internal static void Reset() => Grants.Reset();
}
