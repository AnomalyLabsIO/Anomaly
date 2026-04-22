namespace Anomaly.Client.Api.Events;

public delegate void AnomalyEventHandler<in TEventArgs>(TEventArgs ev) where TEventArgs : EventArgs;