using UnityEngine.SceneManagement;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class SceneLoadedEventArgs : EventArgs
{
    public SceneLoadedEventArgs(Scene scene, LoadSceneMode sceneMode)
    {
        Scene = scene;
        SceneMode = sceneMode;
    }

    public Scene Scene { get; }

    /// <summary>The loaded scene's name, without exposing its native handle to scripts.</summary>
    [ScriptVisible]
    public string SceneName => Scene.name;

    public LoadSceneMode SceneMode { get; }
}
