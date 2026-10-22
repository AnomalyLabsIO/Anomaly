using System.Reflection;
using MonoMod.RuntimeDetour;

namespace Anomaly.Bootstrap;

internal static partial class RuntimeGuard
{
    [ThreadStatic] private static Stack<PendingIlApplication>? _pendingIlApplications;

    private sealed class PendingIlApplication
    {
        internal PendingIlApplication(Operation operation, int callbackDepth)
        { Operation = operation; CallbackDepth = callbackDepth; }
        internal Operation Operation { get; }
        internal int CallbackDepth { get; }
        internal bool CallbackReturned { get; set; }
    }

    private static bool IsIlHookApplicationDispatch(Delegate[] entries) => entries.Length != 0 &&
        Callbacks.Any(slot => slot.Field.DeclaringType == typeof(ILHook) && slot.Field.Name == nameof(ILHook.OnDetour) &&
            slot.Guard.Equals(entries[0]));

    private static bool BeginIlHookApplication(ILHook hook, out PendingIlApplication? application)
    {
        application = null;
        if (_pendingIlApplications?.Count >= 64)
        {
            Invalidate(BootstrapLocalization.Get(TrKeys.Bootstrap_IlHookNestingExceeded));
            return false;
        }
        Operation? operation = null;
        if (!DetourPrefix(hook, GuardRuntimeAdapters.IlHookApply, Array.Empty<object>(), ref operation)) return false;
        if (operation == null)
        {
            Invalidate(BootstrapLocalization.Get(TrKeys.Bootstrap_IlHookOperationMissing));
            return false;
        }
        application = new PendingIlApplication(operation, _foreignCallbackDepth);
        (_pendingIlApplications ??= new()).Push(application);
        return true;
    }

    private static void FinishIlHookCallback(PendingIlApplication application, Exception? failure)
    {
        if (_pendingIlApplications?.Count > 0 && ReferenceEquals(_pendingIlApplications.Peek(), application))
            _pendingIlApplications.Pop();
        else
            Invalidate(BootstrapLocalization.Get(TrKeys.Bootstrap_IlHookCompletionOrderChanged));
        DetourFinalizer(failure, application.Operation);
    }

    private static bool IlHookRefreshPrefix(object __0, ref Operation? __state)
    {
        // Before Ready, ordinary loader installation has not yet attached the
        // guarded callback. No admission evidence is available during that phase.
        if (_installed && !IlHookCallbackIntact())
        {
            Invalidate(BootstrapLocalization.Get(TrKeys.Bootstrap_IlHookCallbackMissing));
            return false;
        }
        if (_pendingIlApplications?.Count > 0)
        {
            PendingIlApplication application = _pendingIlApplications.Peek();
            Operation operation = application.Operation;
            if (application.CallbackReturned && application.CallbackDepth == _foreignCallbackDepth &&
                _operations?.Count > 0 && ReferenceEquals(_operations.Peek(), operation) &&
                operation.Detour is ILHook hook && hook.IsApplied &&
                Equals(GuardRuntimeAdapters.ContextMethod.GetValue(__0), operation.Target))
            {
                _pendingIlApplications.Pop();
                __state = operation;
            }
        }
        return true;
    }

    private static Exception? IlHookRefreshFinalizer(Exception? __exception, Operation? __state) =>
        DetourFinalizer(__exception, __state);

    private static bool IlHookCallbackIntact()
    {
        foreach (var slot in Callbacks)
        {
            if (slot.Field.DeclaringType != typeof(ILHook) || slot.Field.Name != nameof(ILHook.OnDetour)) continue;
            Delegate[] current = ((MulticastDelegate?)slot.Field.GetValue(null))?.GetInvocationList() ?? Array.Empty<Delegate>();
            return current.Length != 0 && current[0].Equals(slot.Guard) &&
                slot.Previous.Where(IsStockShieldCallback).All(current.Contains);
        }
        return false;
    }

    private static void FailAbandonedIlHookApplications()
    {
        // Successful Apply is synchronous: after dispatch it must reach refresh.
        // If a private context lookup throws first, keep admission closed and
        // commit failure evidence at the next ordinary main-thread frame.
        if (_foreignCallbackDepth != 0 || _pendingIlApplications?.Count is not > 0 ||
            !_pendingIlApplications.Peek().CallbackReturned) return;
        Invalidate(BootstrapLocalization.Get(TrKeys.Bootstrap_IlHookRefreshIncomplete));
        while (_pendingIlApplications.Count > 0 && _pendingIlApplications.Peek().CallbackReturned)
        {
            var application = _pendingIlApplications.Pop();
            DetourFinalizer(new InvalidOperationException("Abandoned ILHook application."), application.Operation);
        }
    }
}
