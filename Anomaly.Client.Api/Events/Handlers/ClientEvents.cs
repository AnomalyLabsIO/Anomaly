using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class ClientEvents
{
    public static event AnomalyEventHandler<SceneLoadedEventArgs> SceneLoaded;
    public static event AnomalyEventHandler<ClientConnectedEventArgs> Connected;
    public static event AnomalyEventHandler<ClientDisconnectedEventArgs> Disconnected;
    public static event AnomalyEventHandler<ClientReadyEventArgs> Ready;
    public static event AnomalyEventHandler<CanvasReadyEventArgs> CanvasReady;

    internal static void OnSceneLoaded(SceneLoadedEventArgs args) => EventInvoker.Invoke(SceneLoaded, args);

    internal static void OnConnected(ClientConnectedEventArgs args) => EventInvoker.Invoke(Connected, args);

    internal static void OnDisconnected(ClientDisconnectedEventArgs args) => EventInvoker.Invoke(Disconnected, args);

    internal static void OnReady(ClientReadyEventArgs args) => EventInvoker.Invoke(Ready, args);

    internal static void OnCanvasReady(CanvasReadyEventArgs args) => EventInvoker.Invoke(CanvasReady, args);
}
