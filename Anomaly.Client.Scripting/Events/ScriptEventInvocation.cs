using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting.Events;

/// <summary>A queued registration generation; removing it invalidates pending deliveries.</summary>
internal sealed class ScriptEventInvocation
{
    private readonly ScriptEventRegistration _registration;
    private readonly object?[] _arguments;
    internal ScriptEventInvocation(ScriptEventRegistration registration, object?[] arguments)
    {
        _registration = registration;
        _arguments = (object?[])arguments.Clone();
    }
    internal bool IsActive => _registration.IsActive;
    internal DynValue Callback => _registration.Callback;
    internal string Name => _registration.Source.QualifiedName;
    internal object?[] Arguments => (object?[])_arguments.Clone();

    internal DynValue[] ResolveArguments(Script script)
    {
        _registration.Owner.EnsureOwner(script);
        if (!IsActive) throw new ScriptRuntimeException("The event subscription is no longer active.");
        return _arguments.Select(value => ScriptFrozenEventValue.ToScriptValue(script, value)).ToArray();
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
