using System.Reflection;
using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting.Events;

internal sealed class ScriptEventSource : IDisposable
{
    private readonly object _sync = new();
    private readonly List<ScriptEventRegistration> _registrations = new(256);
    private bool _publishing;
    private readonly ScriptEventSnapshotPlan? _snapshot;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private Delegate? _handler;
    private bool _faulted;
    private bool _disposed;

    internal ScriptEventSource(string category, string name, EventInfo eventInfo, ParameterInfo[] parameters,
        ScriptEventSnapshotPlan? snapshot, bool coalesceLatest = false)
    {
        Category = category;
        Name = name;
        EventInfo = eventInfo;
        Parameters = parameters;
        _snapshot = snapshot;
        CoalesceLatest = coalesceLatest;
        QualifiedName = "Events." + category + "." + name;
        AddName = QualifiedName + ".Add";
        RemoveName = QualifiedName + ".Remove";
    }

    internal string Category { get; }
    internal string Name { get; }
    internal string QualifiedName { get; }
    internal string AddName { get; }
    internal string RemoveName { get; }
    internal EventInfo EventInfo { get; }
    internal ParameterInfo[] Parameters { get; }
    internal bool CoalesceLatest { get; }

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
            catch (Exception exception)
            {
                // A custom accessor may attach before throwing. Roll back once and
                // quarantine this source so repeated attempts cannot add stale bridges.
                _faulted = true;
                lock (_sync) _registrations.Remove(registration);
                registration.Deactivate();
                try { EventInfo.RemoveEventHandler(null, handler); _handler = null; }
                catch (Exception) { }
                ScriptHostException.RethrowFatal(exception);
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
        // Foreign publishers only signal existing registrations. They must not
        // allocate snapshots, inspect arguments or enter an interpreter budget.
        if (Environment.CurrentManagedThreadId != _ownerThread || _publishing)
        {
            lock (_sync)
                foreach (var registration in _registrations)
                    if (registration.IsActive) registration.Owner.RequestRetirement(eventName: QualifiedName);
            return;
        }
        _publishing = true;
        var firstMemory = default(MemoryBudget.AllocationReservation);
        Delivery[]? deliveries = null;
        var count = 0;
        try
        {
            ScriptEventRegistration? first = null;
            lock (_sync)
                foreach (var registration in _registrations)
                    if (registration.IsActive && registration.Owner.TryReservePublication(registration, out firstMemory))
                    { first = registration; break; }
            if (first == null) return;
            // No publication array/list/anchor is allocated until a recipient
            // has reserved the maximum bounded snapshot and delivery workspace.
            var allocationOwner = new object();
            firstMemory.Bind(allocationOwner);
            lock (_sync)
            {
                deliveries = new Delivery[_registrations.Count];
                deliveries[count++] = new Delivery(first, firstMemory);
                foreach (var registration in _registrations)
                    if (!ReferenceEquals(registration, first) && registration.IsActive &&
                        registration.Owner.TryReservePublication(registration, out var reservation))
                    {
                        reservation.Bind(allocationOwner);
                        deliveries[count++] = new Delivery(registration, reservation);
                    }
            }
            object?[] arguments;
            try { arguments = _snapshot == null ? Array.Empty<object?>() : new[] { _snapshot.Capture(value, allocationOwner) }; }
            catch (Exception exception)
            {
                for (var index = 0; index < count; index++) deliveries[index].Registration.Owner.RequestRetirement(
                    exception is InvalidOperationException ? exception.Message : "The approved event snapshot getter failed; session retired.",
                    QualifiedName);
                ScriptHostException.RethrowFatal(exception);
                return;
            }
            for (var index = 0; index < count; index++)
            {
                var registration = deliveries[index].Registration;
                if (!registration.IsActive) continue;
                try { registration.Owner.Publish(registration, arguments, allocationOwner); }
                catch (Exception exception)
                {
                    for (var recipient = 0; recipient < count; recipient++)
                        deliveries[recipient].Registration.Owner.RequestRetirement(
                            "The approved event delivery failed; session retired.", QualifiedName);
                    ScriptHostException.RethrowFatal(exception);
                }
            }
            GC.KeepAlive(allocationOwner);
        }
        catch (Exception exception)
        {
            // Bookkeeping and binding can also fail before snapshot capture.
            // Retire existing registrations without allocating a recovery array.
            lock (_sync)
                foreach (var registration in _registrations)
                    registration.Owner.RequestRetirement("The approved event publication failed; session retired.", QualifiedName);
            ScriptHostException.RethrowFatal(exception);
        }
        finally
        {
            firstMemory.Dispose();
            if (deliveries != null)
                for (var index = 0; index < count; index++) deliveries[index].Memory.Dispose();
            _publishing = false;
        }
    }

    private readonly record struct Delivery(ScriptEventRegistration Registration, MemoryBudget.AllocationReservation Memory);

    private void Detach()
    {
        if (_handler == null) return;
        try { EventInfo.RemoveEventHandler(null, _handler); _handler = null; }
        catch (Exception exception) { _faulted = true; ScriptHostException.RethrowFatal(exception); throw; }
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
