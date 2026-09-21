using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class ClientEvents
{
    public static event AnomalyEventHandler<SceneLoadedEventArgs> SceneLoaded;
    public static event AnomalyEventHandler<ClientConnectedEventArgs> Connected;

    /// <summary>
    /// Reports a transport disconnect, including unsuccessful handshake and retry
    /// attempts that never reached <see cref="Ready"/>. Cleanup handlers must be
    /// idempotent; do not count this as one event per completed gameplay session.
    /// </summary>
    public static event AnomalyEventHandler<ClientDisconnectedEventArgs> Disconnected;

    /// <summary>
    /// Reports the client's scene-ready lifecycle callback. This can precede the
    /// local player hub, populated world collections, and server enrollment disclosure.
    /// </summary>
    /// <remarks>
    /// Raised from the network manager's OnClientSceneChanged callback and may
    /// occur again after a scene transition. Check the state each operation needs,
    /// including <see cref="Anomaly.Client.Api.Features.Client.LocalPlayer"/> and
    /// <see cref="Anomaly.Client.Api.Features.Client.EnrollmentId"/>; neither is
    /// guaranteed to be available here. Events are not replayed to late subscribers.
    /// </remarks>
    public static event AnomalyEventHandler<ClientReadyEventArgs> Ready;
    public static event AnomalyEventHandler<CanvasReadyEventArgs> CanvasReady;

    internal static void OnSceneLoaded(SceneLoadedEventArgs args) => EventInvoker.Invoke(SceneLoaded, args);

    internal static void OnConnected(ClientConnectedEventArgs args) => EventInvoker.Invoke(Connected, args);

    internal static void OnDisconnected(ClientDisconnectedEventArgs args) => EventInvoker.Invoke(Disconnected, args);

    internal static void OnReady(ClientReadyEventArgs args) => EventInvoker.Invoke(Ready, args);

    internal static void OnCanvasReady(CanvasReadyEventArgs args) => EventInvoker.Invoke(CanvasReady, args);
}
