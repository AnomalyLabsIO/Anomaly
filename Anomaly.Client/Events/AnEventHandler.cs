namespace Anomaly.Client.Events;

public delegate void AnEventHandler<in TEventArgs>(TEventArgs ev) where TEventArgs : EventArgs;