using System.Reflection;

namespace Anomaly.Client.Scripting.Events;

internal sealed class ScriptEventSource : IDisposable
{
    private readonly object _sync = new();
    private readonly List<ScriptEventRegistration> _registrations = new();
    private readonly ScriptEventSnapshotPlan? _snapshot;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private Delegate? _handler;
    private bool _faulted;
    private bool _disposed;

    internal ScriptEventSource(string category, string name, EventInfo eventInfo, ParameterInfo[] parameters,
        ScriptEventSnapshotPlan? snapshot)
    {
        Category = category;
        Name = name;
        EventInfo = eventInfo;
        Parameters = parameters;
        _snapshot = snapshot;
    }

    internal string Category { get; }
    internal string Name { get; }
    internal string QualifiedName => "Events." + Category + "." + Name;
    internal EventInfo EventInfo { get; }
    internal ParameterInfo[] Parameters { get; }

    internal void Add(ScriptEventRegistration registration)
    {
        EnsureOwner();
        if (_disposed || _faulted) throw new InvalidOperationException("The event bridge is unavailable.");
        lock (_sync)
        {
            if (_registrations.Count >= 256) throw new InvalidOperationException("The event bridge exceeded its callback limit.");
            _registrations.Add(registration);
        }
        if (_handler == null)
        {
            var method = typeof(ScriptEventSource).GetMethod(Parameters.Length == 0 ? nameof(PublishEmpty) : nameof(PublishValue),
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (Parameters.Length != 0) method = method.MakeGenericMethod(Parameters[0].ParameterType);
            var handler = Delegate.CreateDelegate(EventInfo.EventHandlerType!, this, method);
            _handler = handler;
            try { EventInfo.AddEventHandler(null, handler); }
            catch
            {
                // A custom accessor may attach before throwing. Roll back once and
                // quarantine this source so repeated attempts cannot add stale bridges.
                _faulted = true;
                lock (_sync) _registrations.Remove(registration);
                registration.Deactivate();
                try { EventInfo.RemoveEventHandler(null, handler); _handler = null; }
                catch (Exception) { }
                throw;
            }
        }
        bool stillRegistered;
        lock (_sync) stillRegistered = _registrations.Contains(registration);
        if (registration.Owner.IsUnavailable || !stillRegistered)
        {
            Remove(registration);
            throw new InvalidOperationException("The session was retired while subscribing to its event.");
        }
        registration.Activate();
    }

    internal void Remove(ScriptEventRegistration registration)
    {
        EnsureOwner();
        registration.Deactivate();
        bool empty;
        lock (_sync) { _registrations.Remove(registration); empty = _registrations.Count == 0; }
        if (empty) Detach();
    }

    private void PublishEmpty() => Publish(null);
    private void PublishValue<T>(T value) => Publish(value);

    private void Publish(object? value)
    {
        ScriptEventRegistration[] registrations;
        lock (_sync) registrations = _registrations.Where(value => value.IsActive).ToArray();
        if (registrations.Length == 0) return;
        if (Environment.CurrentManagedThreadId != _ownerThread)
        {
            foreach (var registration in registrations) registration.Owner.RequestRetirement();
            return;
        }
        object?[] arguments;
        try { arguments = _snapshot == null ? Array.Empty<object?>() : new[] { _snapshot.Capture(value) }; }
        catch (Exception exception) when (exception is not OutOfMemoryException && exception is not StackOverflowException)
        {
            foreach (var registration in registrations) registration.Owner.RequestRetirement();
            return;
        }
        foreach (var registration in registrations)
        {
            if (!registration.IsActive) continue;
            try { registration.Owner.Enqueue(new ScriptEventInvocation(registration, arguments)); }
            catch (Exception exception) when (exception is not OutOfMemoryException && exception is not StackOverflowException)
            { registration.Owner.RequestRetirement(); }
        }
    }

    private void Detach()
    {
        if (_handler == null) return;
        try { EventInfo.RemoveEventHandler(null, _handler); _handler = null; }
        catch { _faulted = true; throw; }
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        lock (_sync)
        {
            foreach (var registration in _registrations) registration.Deactivate();
            _registrations.Clear();
        }
        Detach();
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Event bridges must be managed on their owner thread.");
    }
}
