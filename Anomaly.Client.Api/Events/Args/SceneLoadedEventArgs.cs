using UnityEngine.SceneManagement;

namespace Anomaly.Client.Api.Events.Args;

public class SceneLoadedEventArgs : EventArgs
{
    public SceneLoadedEventArgs(Scene scene, LoadSceneMode sceneMode)
    {
        Scene = scene;
        SceneMode = sceneMode;
    }

    public Scene Scene { get; }

    public LoadSceneMode SceneMode { get; }
}