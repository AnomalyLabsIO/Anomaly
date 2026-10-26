using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting.Events;

/// <summary>A queued registration generation; removing it invalidates pending deliveries.</summary>
internal sealed class ScriptEventInvocation : IQueuedScriptInvocation
{
    private readonly ScriptEventRegistration _registration;
    private readonly object?[] _arguments;
    private readonly object? _allocationOwner;
    internal ScriptEventInvocation(ScriptEventRegistration registration, object?[] arguments, object? allocationOwner = null)
    {
        _registration = registration;
        _arguments = (object?[])arguments.Clone();
        _allocationOwner = allocationOwner;
    }
    public bool IsActive => _registration.IsActive;
    public DynValue Callback => _registration.Callback;
    internal string Name => _registration.Source.QualifiedName;
    internal object?[] Arguments => (object?[])_arguments.Clone();

    public DynValue[] ResolveArguments(Script script)
    {
        _registration.Owner.EnsureOwner(script);
        if (!IsActive) throw new ScriptRuntimeException("The event subscription is no longer active.");
        using var memoryScope = script.MemoryBudget?.EnterScope();
        var converted = MemoryBudget.Allocate(32L + 8L * _arguments.Length, () => new DynValue[_arguments.Length]);
        for (var index = 0; index < _arguments.Length; index++)
            converted[index] = ScriptFrozenEventValue.ToScriptValue(script, _arguments[index]);
        return converted;
    }
}

internal sealed class ScriptEventRegistration
{
    private int _active;
    internal ScriptEventRegistration(ScriptEventSubscriptions owner, ScriptEventSource source, string token, DynValue callback)
    {
        Owner = owner;
        Source = source;
        Token = token;
        Callback = callback.Clone();
    }
    internal ScriptEventSubscriptions Owner { get; }
    internal ScriptEventSource Source { get; }
    internal string Token { get; }
    internal DynValue Callback { get; private set; }
    internal bool IsActive => Volatile.Read(ref _active) != 0 && !Owner.IsUnavailable;
    internal void Activate() => Volatile.Write(ref _active, 1);
    internal void Deactivate() { Volatile.Write(ref _active, 0); Callback = DynValue.Nil; }
}
