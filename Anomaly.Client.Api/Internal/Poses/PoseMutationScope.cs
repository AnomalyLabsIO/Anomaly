namespace Anomaly.Client.Api.Internal.Poses;

/// <summary>Rollback journal for short synchronous native queries. All restorations run even if one fails.</summary>
internal sealed class PoseMutationScope : IDisposable
{
    private readonly List<Action> _restore = new();
    private readonly Action _synchronize;
    private bool _disposed;
    private PoseMutationScope(Action synchronize) => _synchronize = synchronize;
    internal static PoseMutationScope Begin(Action<PoseMutationScope> apply, Action synchronize = null)
    {
        var scope = new PoseMutationScope(synchronize);
        try
        {
            apply(scope);
            synchronize?.Invoke();
            return scope;
        }
        catch
        {
            try { scope.Dispose(); } catch { /* Preserve the acquisition failure after attempting every restore. */ }
            throw;
        }
    }
    internal void AddRestore(Action restore)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PoseMutationScope));
        _restore.Add(restore ?? throw new ArgumentNullException(nameof(restore)));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception> failures = null;
        for (int i = _restore.Count - 1; i >= 0; i--)
            try { _restore[i](); } catch (Exception exception) { (failures ??= new()).Add(exception); }
        _restore.Clear();
        try { _synchronize?.Invoke(); } catch (Exception exception) { (failures ??= new()).Add(exception); }
        if (failures != null) throw new AggregateException("Native pose restoration failed.", failures);
    }
}
