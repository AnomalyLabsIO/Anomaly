using UnityEngine;

namespace Anomaly.Client.Events.Args;

public class CanvasReadyEventArgs : EventArgs
{
    public CanvasReadyEventArgs(GameObject canvas)
    {
        Canvas = canvas;
    }

    public GameObject Canvas { get; private set; }
}