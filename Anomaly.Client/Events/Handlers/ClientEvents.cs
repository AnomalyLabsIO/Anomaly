using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class ClientEvents
{
    public static event AnEventHandler<SceneLoadedEventArgs> SceneLoaded;
    public static event AnEventHandler<ClientConnectedEventArgs> Connected;
    public static event AnEventHandler<ClientDisconnectedEventArgs> Disconnected;
    public static event AnEventHandler<ClientReadyEventArgs> Ready;
    public static event AnEventHandler<CanvasReadyEventArgs> CanvasReady;
    public static event AnEventHandler<CreditHookEventArgs> CreditHook;

    internal static void OnSceneLoaded(SceneLoadedEventArgs args) => EventInvoker.Invoke(SceneLoaded, args);

    internal static void OnConnected(ClientConnectedEventArgs args) => EventInvoker.Invoke(Connected, args);

    internal static void OnDisconnected(ClientDisconnectedEventArgs args) => EventInvoker.Invoke(Disconnected, args);

    internal static void OnReady(ClientReadyEventArgs args) => EventInvoker.Invoke(Ready, args);

    internal static void OnCanvasReady(CanvasReadyEventArgs args) => EventInvoker.Invoke(CanvasReady, args);

    internal static void OnCreditHook(CreditHookEventArgs args) => EventInvoker.Invoke(CreditHook, args);
}
