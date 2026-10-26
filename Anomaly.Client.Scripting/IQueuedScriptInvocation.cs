using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting;

/// <summary>One owner-bound registration generation dispatched by the common session queue.</summary>
internal interface IQueuedScriptInvocation
{
    bool IsActive { get; }
    DynValue Callback { get; }
    DynValue[] ResolveArguments(Script script);
}

/// <summary>Events and messages share the same bounded per-session registration pool.</summary>
internal sealed class ScriptSubscriptionBudget
{
    private int _count;
    internal bool TryAcquire()
    {
        if (_count == 32) return false;
        _count++;
        return true;
    }
    internal void Release()
    {
        if (_count > 0) _count--;
    }
}
