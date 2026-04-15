namespace Anomaly.Client.Events;

public delegate void AnomalyEventHandler<in TEventArgs>(TEventArgs ev) where TEventArgs : EventArgs;