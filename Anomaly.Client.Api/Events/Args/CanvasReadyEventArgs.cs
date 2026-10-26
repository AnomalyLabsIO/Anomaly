using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class CanvasReadyEventArgs : EventArgs
{
    public CanvasReadyEventArgs(GameObject canvas)
    {
        Canvas = canvas;
    }

    public GameObject Canvas { get; private set; }
}
