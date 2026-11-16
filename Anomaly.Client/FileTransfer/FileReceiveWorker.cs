using System.Collections.Concurrent;
using System.Threading;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Client.FileTransfer;

internal interface IFileReceiveBuffer : IDisposable
{
    bool IsComplete { get; }
    bool TryAddChunk(ushort index, ushort total, byte[] data, out bool duplicate, out string error);
    bool CommitToCache(CancellationToken cancellation);
}

internal sealed class FileReceiveFile
{
    internal string Key;
    internal byte[] Hash;
    internal string Path;
    internal uint Size;
    internal byte Attempt;
}

internal enum FileReceiveResultKind { Prepared, Written, Finished, Failed }

internal sealed class FileReceiveResult
{
    internal long Epoch;
    internal ulong SessionId;
    internal FileReceiveResultKind Kind;
    internal FileReceiveFile File;
    internal byte Domain;
    internal uint FrameCounter;
    internal int Bytes;
    internal bool Success;
    internal string Error;
    internal List<FileReceiveFile> Missing;
}

/// <summary>
/// One bounded disk owner. No stream creation, seek, flush, hashing, import or
/// disposal runs on the game thread. Cancellation never waits for disk work.
/// </summary>
internal sealed class FileReceiveWorker : IDisposable
{
    private sealed class Work
    {
        internal FileReceiveFile File;
        internal ushort Index, Total;
        internal byte Domain;
        internal uint FrameCounter;
        internal byte[] Data;
    }

    private sealed class ActiveFile
    {
        internal FileReceiveFile File;
        internal IFileReceiveBuffer Buffer;
        internal bool CompletionReceived;
    }

    private readonly object _gate = new();
    private readonly Queue<Work> _queue = new();
    private readonly ConcurrentQueue<FileReceiveResult> _results = new();
    private readonly AutoResetEvent _available = new(false);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<FileReceiveFile, IFileReceiveBuffer> _open;
    private readonly Func<byte[], bool> _hasFile;
    private readonly List<FileReceiveFile> _files;
    private readonly Dictionary<string, ActiveFile> _active = new(StringComparer.Ordinal);
    private readonly long _epoch;
    private readonly ulong _sessionId;
    private int _pendingFrames, _pendingBytes, _disposed;
    private bool _processing = true;
    private int _stopped;
    private readonly Thread _thread;

    internal FileReceiveWorker(long epoch, ulong sessionId, List<FileReceiveFile> files,
        Func<byte[], bool> hasFile, Func<FileReceiveFile, IFileReceiveBuffer> open)
    {
        _epoch = epoch;
        _sessionId = sessionId;
        _files = files;
        _hasFile = hasFile;
        _open = open;
        _thread = new Thread(Run) { IsBackground = true, Name = "Anomaly file receive storage" };
        _thread.Start();
    }

    internal bool TryRead(out FileReceiveResult result) => _results.TryDequeue(out result);
    internal bool IsIdle { get { lock (_gate) return !_processing && _queue.Count == 0 && _results.IsEmpty; } }
    internal bool IsStopped => Volatile.Read(ref _stopped) != 0;

    internal bool TryEnqueue(FileReceiveFile file, ushort index, ushort total, byte[] data, byte domain, uint frameCounter = 0)
    {
        lock (_gate)
        {
            if (_disposed != 0 || _queue.Count >= BulkTransferProtocol.MaxWindowFrames + BulkTransferProtocol.MaxFilesInFlight)
                return false;
            if (data != null && (_pendingFrames >= BulkTransferProtocol.MaxWindowFrames ||
                data.Length > BulkTransferProtocol.MaxWindowBytes - _pendingBytes)) return false;
            _queue.Enqueue(new Work { File = file, Index = index, Total = total, Data = data,
                Domain = domain, FrameCounter = frameCounter });
            if (data != null) { _pendingFrames++; _pendingBytes += data.Length; }
            _available.Set();
            return true;
        }
    }

    private void Run()
    {
        try
        {
            var missing = new List<FileReceiveFile>();
            foreach (var file in _files)
            {
                _stop.Token.ThrowIfCancellationRequested();
                if (!_hasFile(file.Hash)) missing.Add(file);
            }
            Publish(new FileReceiveResult { Kind = FileReceiveResultKind.Prepared, Missing = missing });
            lock (_gate) _processing = false;
            while (!_stop.IsCancellationRequested)
            {
                Work work = null;
                lock (_gate)
                    if (_queue.Count != 0) { work = _queue.Dequeue(); _processing = true; }
                if (work == null) { _available.WaitOne(); continue; }
                try { Process(work); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    Publish(new FileReceiveResult { Kind = FileReceiveResultKind.Failed, File = work.File,
                        Error = exception.GetType().Name });
                    return;
                }
                finally
                {
                    if (work.Data != null)
                        lock (_gate) { _pendingFrames--; _pendingBytes -= work.Data.Length; }
                    lock (_gate) _processing = false;
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Publish(new FileReceiveResult { Kind = FileReceiveResultKind.Failed, Error = exception.GetType().Name });
        }
        finally
        {
            lock (_gate) _disposed = 1;
            foreach (var file in _active.Values)
                try { file.Buffer.Dispose(); } catch { /* Continue releasing every owned file. */ }
            _active.Clear();
            lock (_gate) { _queue.Clear(); _pendingBytes = _pendingFrames = 0; }
            // The producer and the worker serialize signal ownership with _gate.
            lock (_gate) { _disposed = 1; _available.Dispose(); }
            Volatile.Write(ref _stopped, 1);
        }
    }

    private void Process(Work work)
    {
        _stop.Token.ThrowIfCancellationRequested();
        var file = work.File;
        if (!_active.TryGetValue(file.Key, out var active))
        {
            if (_active.Count >= BulkTransferProtocol.MaxFilesInFlight)
                throw new InvalidDataException("The receive storage file pipeline is full.");
            active = new ActiveFile { File = file, Buffer = _open(file) };
            _active.Add(file.Key, active);
        }
        if (active.File.Attempt != file.Attempt)
            throw new InvalidDataException("A stale storage attempt entered the receive queue.");
        if (work.Data == null) active.CompletionReceived = true;
        else
        {
            bool accepted = active.Buffer.TryAddChunk(work.Index, work.Total, work.Data, out bool duplicate, out string error);
            if (!accepted && !duplicate) throw new InvalidDataException(error ?? "Invalid file storage chunk.");
            if (accepted)
                Publish(new FileReceiveResult { Kind = FileReceiveResultKind.Written, File = file,
                    Domain = work.Domain, FrameCounter = work.FrameCounter, Bytes = work.Data.Length });
        }
        if (!active.CompletionReceived || !active.Buffer.IsComplete) return;
        bool success;
        string failure = null;
        try { success = active.Buffer.CommitToCache(_stop.Token); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw; }
        catch (Exception exception) { success = false; failure = exception.GetType().Name; }
        active.Buffer.Dispose();
        _active.Remove(file.Key);
        Publish(new FileReceiveResult { Kind = FileReceiveResultKind.Finished, File = file, Success = success, Error = failure });
    }

    private void Publish(FileReceiveResult result)
    {
        if (_stop.IsCancellationRequested) return;
        result.Epoch = _epoch;
        result.SessionId = _sessionId;
        _results.Enqueue(result);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed != 0) return;
            _disposed = 1;
            _stop.Cancel();
            _queue.Clear();
            _available.Set();
        }
    }
}
