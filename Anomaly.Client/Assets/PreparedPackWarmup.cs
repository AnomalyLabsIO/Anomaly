using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Anomaly.Client.Assets;

/// <summary>
/// Owns serial background preparation and its leases until the final transaction
/// takes them. Retirement cancels immediately and releases untaken results off-thread.
/// </summary>
internal sealed class PreparedPackWarmup<T> : IDisposable where T : class, IDisposable
{
    private sealed class Preparation
    {
        internal Func<CancellationToken, T> Prepare;
        internal CancellationToken Token;
    }

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, Task<T>> _work = new(StringComparer.Ordinal);
    private Task _tail = Task.CompletedTask;
    private bool _disposed;

    internal void Schedule(string key, Func<CancellationToken, T> prepare)
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PreparedPackWarmup<T>));
            if (_work.ContainsKey(key)) return;
            if (_work.Count >= 128) throw new InvalidDataException("Too many prepared packs in one offer.");
            var previous = _tail;
            var token = _stop.Token;
            var task = previous.ContinueWith<T>(RunPreparation,
                new Preparation { Prepare = prepare, Token = token },
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            _work.Add(key, task);
            _tail = task;
        }
    }

    private static T RunPreparation(Task completed, object state)
    {
        // One rejected pack must not execute other validators in parallel.
        // Its failure is still retained and reported by the final transaction.
        _ = completed.Exception;
        var preparation = (Preparation)state;
        preparation.Token.ThrowIfCancellationRequested();
        return preparation.Prepare(preparation.Token);
    }

    // Called only by a preparation worker, never by the game thread.
    internal T Take(string key, CancellationToken cancellation)
    {
        Task<T> task;
        lock (_gate)
        {
            if (_disposed) throw new OperationCanceledException();
            if (!_work.TryGetValue(key, out task)) return null;
        }
        cancellation.ThrowIfCancellationRequested();
        T prepared = task.GetAwaiter().GetResult();
        lock (_gate)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_disposed || !_work.TryGetValue(key, out var owned) || !ReferenceEquals(task, owned))
                throw new OperationCanceledException();
            _work.Remove(key); // One ownership transfer; retirement no longer disposes this lease.
            return prepared;
        }
    }

    public void Dispose()
    {
        Task<T>[] pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            pending = _work.Values.ToArray();
            _work.Clear();
        }
        _stop.Cancel();
        Task completion = Task.WhenAll(pending);
        _ = completion.ContinueWith(DisposePending, pending,
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void DisposePending(Task completed, object state)
    {
        _ = completed.Exception;
        foreach (var task in (Task<T>[])state)
            if (task.Status == TaskStatus.RanToCompletion)
                try { task.Result?.Dispose(); } catch { /* Continue retiring the complete set. */ }
        _stop.Dispose();
    }
}
