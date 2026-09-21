using System;
using System.Threading;
using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Config;
using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Persistence;
using Anomaly.Client.Api.Scheduling;

namespace Anomaly.Client.Api.Modding;

public interface IModContext
{
    string ModId { get; }
    string DataDirectory { get; }
    string ConfigDirectory { get; }
    CancellationToken CancellationToken { get; }
    IConfigSource Config { get; }
    IPersistenceStore Persistence { get; }

    ScheduledTask Schedule(TimeSpan delay, Action callback);
    ScheduledTask ScheduleRecurring(TimeSpan interval, Action callback, TimeSpan firstDelay = default);
    void RegisterCommand(ICommand command);
    /// <summary>
    /// Registers an Anomaly event through owner fault containment and guarantees
    /// reverse-order unsubscription of the exact wrapped delegate.
    /// </summary>
    void Subscribe<TEventArgs>(
        Action<AnomalyEventHandler<TEventArgs>> subscribe,
        Action<AnomalyEventHandler<TEventArgs>> unsubscribe,
        AnomalyEventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs;
    void Track(IDisposable resource);
    void RegisterCleanup(Action cleanup);
    void ReportHealth(string featureId, FeatureHealthStatus status, string detail = "");
}
