using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Anomaly.Server.Networking;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using MEC;
using Mirror;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.FileTransfer;

public class FileTransferSession
{
    private enum State
    {
        AwaitingRequest,
        Sending,
        AwaitingAck,
        Complete,
        Aborted
    }

    private static readonly Dictionary<ReferenceHub, FileTransferSession> ActiveSessions =
        new Dictionary<ReferenceHub, FileTransferSession>();
    private static readonly object BudgetLock = new object();

    private const int MaxActiveTransfers = 8;
    private const long MaxGlobalReservedBytes = 512L * 1024L * 1024L;
    private const int DefaultChunkIntervalMs = 5;
    private const int MaxRetries = 2;
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(30);

    private static int _reservedTransferCount;
    private static long _globalReservedBytes;

    public static event Action<ReferenceHub> SessionCompleted;

    private readonly ReferenceHub _hub;
    private readonly ulong _sessionId;
    private readonly FileManifestMessage _manifestMessage;
    private readonly Dictionary<string, ServerAssetManager.AssetInfo> _manifest;
    private readonly Queue<ServerAssetManager.AssetInfo> _pendingFiles =
        new Queue<ServerAssetManager.AssetInfo>();

    private State _state = State.AwaitingRequest;
    private ServerAssetManager.AssetInfo _currentFile;
    private bool _hasCurrentFile;
    private int _retryCount;
    private long _reservedBytes;
    private bool _budgetReserved;
    private DateTime _lastActivityUtc;
    private CoroutineHandle _sendHandle;
    private CoroutineHandle _watchdogHandle;

    private FileTransferSession(ReferenceHub hub)
    {
        _hub = hub;
        _sessionId = CreateSessionId();
        _manifestMessage = ServerAssetManager.BuildManifest(_sessionId, out var snapshot);
        _manifest = snapshot;
        _lastActivityUtc = DateTime.UtcNow;
    }

    public static void StartForPlayer(ReferenceHub hub)
    {
        if (hub == null)
            return;

        CleanupPlayer(hub);
        var session = new FileTransferSession(hub);
        ActiveSessions[hub] = session;
        session.SendManifest();
    }

    public static void CleanupPlayer(ReferenceHub hub)
    {
        if (hub != null && ActiveSessions.TryGetValue(hub, out var session))
            session.EndSession(false, "player disconnected or session was replaced");
    }

    public static void Reset()
    {
        var sessions = new List<FileTransferSession>(ActiveSessions.Values);
        foreach (var session in sessions)
            session.EndSession(false, "server reset");

        ActiveSessions.Clear();
        lock (BudgetLock)
        {
            _reservedTransferCount = 0;
            _globalReservedBytes = 0;
        }
    }

    public static void HandleMessage(ReferenceHub sender, IAnomalyMessage msg)
    {
        if (sender == null || !ActiveSessions.TryGetValue(sender, out var session))
            return;

        switch (msg)
        {
            case FileRequestMessage request:
                session.HandleFileRequest(request);
                break;
            case FileAckMessage ack:
                session.HandleFileAck(ack);
                break;
        }
    }

    private void SendManifest()
    {
        try
        {
            AnomalyMessaging.SendToClient(_hub, _manifestMessage);
            _lastActivityUtc = DateTime.UtcNow;
            _watchdogHandle = Timing.RunCoroutine(Watchdog());
            Logger.Info(
                $"FileTransfer: Sent bounded manifest ({_manifestMessage.Files.Count} files, session={_sessionId:x16}) " +
                $"to {_hub.LoggedNameFromRefHub()}.");
        }
        catch (Exception ex)
        {
            EndSession(false, $"manifest send failed: {ex.Message}");
        }
    }

    private void HandleFileRequest(FileRequestMessage request)
    {
        if (_state != State.AwaitingRequest)
        {
            Logger.Warn($"FileTransfer: Rejected duplicate/out-of-state request from {_hub.LoggedNameFromRefHub()}.");
            return;
        }
        if (request.SessionId != _sessionId)
        {
            EndSession(false, "request session id did not match the advertised manifest");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        long requestedBytes = 0;
        foreach (var hash in request.RequestedHashes)
        {
            string hex = ServerAssetManager.ToHex(hash);
            if (!seen.Add(hex))
            {
                EndSession(false, $"request repeated hash {hex}");
                return;
            }
            if (!_manifest.TryGetValue(hex, out var asset))
            {
                EndSession(false, $"request referenced unadvertised hash {hex}");
                return;
            }

            requestedBytes += asset.Size;
            if (requestedBytes > FileManifestMessage.MaxAggregateBytes)
            {
                EndSession(false, "request exceeded the per-client byte budget");
                return;
            }
            _pendingFiles.Enqueue(asset);
        }

        _lastActivityUtc = DateTime.UtcNow;
        if (_pendingFiles.Count == 0)
        {
            EndSession(true, "all advertised files were already cached");
            return;
        }

        if (!TryReserveBudget(requestedBytes))
        {
            EndSession(false, "global transfer budget is currently exhausted");
            return;
        }

        Logger.Info(
            $"FileTransfer: Accepted {_pendingFiles.Count} files ({requestedBytes} bytes, session={_sessionId:x16}) " +
            $"for {_hub.LoggedNameFromRefHub()}.");
        SendNextFile();
    }

    private void SendNextFile()
    {
        if (_state == State.Aborted || _state == State.Complete)
            return;

        if (_pendingFiles.Count == 0)
        {
            EndSession(true, "all requested files were verified by the client");
            return;
        }

        _currentFile = _pendingFiles.Dequeue();
        _hasCurrentFile = true;
        _retryCount = 0;
        StartCurrentFile();
    }

    private void StartCurrentFile()
    {
        if (!_hasCurrentFile)
        {
            EndSession(false, "internal transfer state lost the current file");
            return;
        }

        _state = State.Sending;
        _lastActivityUtc = DateTime.UtcNow;
        if (_sendHandle.IsRunning)
            Timing.KillCoroutines(_sendHandle);
        _sendHandle = Timing.RunCoroutine(SendFileChunks(_currentFile));
    }

    private IEnumerator<float> SendFileChunks(ServerAssetManager.AssetInfo asset)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(asset.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex)
        {
            EndSession(false, $"failed to open '{asset.RelativePath}': {ex.Message}", false);
            yield break;
        }

        using (stream)
        using (var sha = SHA256.Create())
        {
            long streamLength;
            try
            {
                streamLength = stream.Length;
            }
            catch (Exception ex)
            {
                EndSession(false, $"failed to inspect '{asset.RelativePath}': {ex.Message}", false);
                yield break;
            }

            if (streamLength != asset.Size)
            {
                EndSession(false, $"asset changed size after manifest: '{asset.RelativePath}'", false);
                yield break;
            }

            int totalChunks = checked((int)((asset.Size + FileChunkMessage.MaxChunkBytes - 1) /
                                            FileChunkMessage.MaxChunkBytes));
            if (totalChunks <= 0 || totalChunks > FileChunkMessage.MaxChunksPerFile)
            {
                EndSession(false, $"asset exceeded the chunk-count limit: '{asset.RelativePath}'", false);
                yield break;
            }

            for (int i = 0; i < totalChunks; i++)
            {
                if (_state != State.Sending)
                    yield break;
                if (_hub.connectionToClient == null || !_hub.connectionToClient.isReady)
                {
                    EndSession(false, "connection was lost during transfer", false);
                    yield break;
                }

                int expectedLength = (int)Math.Min(
                    FileChunkMessage.MaxChunkBytes,
                    (long)asset.Size - stream.Position);
                var data = new byte[expectedLength];
                try
                {
                    int read = 0;
                    while (read < expectedLength)
                    {
                        int current = stream.Read(data, read, expectedLength - read);
                        if (current == 0)
                            break;
                        read += current;
                    }
                    if (read != expectedLength)
                    {
                        EndSession(false, $"asset was truncated during transfer: '{asset.RelativePath}'", false);
                        yield break;
                    }

                    sha.TransformBlock(data, 0, data.Length, data, 0);
                    AnomalyMessaging.SendToClient(_hub, new FileChunkMessage
                    {
                        SessionId = _sessionId,
                        Hash = (byte[])asset.Hash.Clone(),
                        ChunkIndex = checked((ushort)i),
                        TotalChunks = checked((ushort)totalChunks),
                        Data = data
                    });
                }
                catch (Exception ex)
                {
                    EndSession(false, $"failed to stream '{asset.RelativePath}': {ex.Message}", false);
                    yield break;
                }
                _lastActivityUtc = DateTime.UtcNow;
                yield return Timing.WaitForSeconds(DefaultChunkIntervalMs / 1000f);
            }

            try
            {
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            }
            catch (Exception ex)
            {
                EndSession(false, $"failed to verify '{asset.RelativePath}': {ex.Message}", false);
                yield break;
            }
            if (!HashesEqual(sha.Hash, asset.Hash))
            {
                EndSession(false, $"asset changed content after manifest: '{asset.RelativePath}'", false);
                yield break;
            }

            AnomalyMessaging.SendToClient(_hub, new FileTransferCompleteMessage
            {
                SessionId = _sessionId,
                Hash = (byte[])asset.Hash.Clone()
            });
            _state = State.AwaitingAck;
            _lastActivityUtc = DateTime.UtcNow;
        }
    }

    private void HandleFileAck(FileAckMessage ack)
    {
        if (_state != State.AwaitingAck || !_hasCurrentFile)
        {
            Logger.Warn($"FileTransfer: Rejected unsolicited/out-of-state ACK from {_hub.LoggedNameFromRefHub()}.");
            return;
        }
        if (ack.SessionId != _sessionId || !HashesEqual(ack.Hash, _currentFile.Hash))
        {
            EndSession(false, "ACK did not match the active session and file");
            return;
        }

        _lastActivityUtc = DateTime.UtcNow;
        if (ack.Status == 0)
        {
            _hasCurrentFile = false;
            SendNextFile();
            return;
        }

        _retryCount++;
        if (_retryCount > MaxRetries)
        {
            EndSession(false, $"client failed integrity verification for '{_currentFile.RelativePath}' after {MaxRetries} retries");
            return;
        }

        Logger.Warn(
            $"FileTransfer: Matched failure ACK for '{_currentFile.RelativePath}'; " +
            $"retrying {_retryCount}/{MaxRetries}.");
        StartCurrentFile();
    }

    private IEnumerator<float> Watchdog()
    {
        while (_state != State.Complete && _state != State.Aborted)
        {
            yield return Timing.WaitForSeconds(1f);
            if (DateTime.UtcNow - _lastActivityUtc > SessionTimeout)
            {
                EndSession(false, $"timed out while {_state.ToString().ToLowerInvariant()}", false);
                yield break;
            }
        }
    }

    private bool TryReserveBudget(long bytes)
    {
        lock (BudgetLock)
        {
            if (_reservedTransferCount >= MaxActiveTransfers ||
                bytes < 0 ||
                _globalReservedBytes + bytes > MaxGlobalReservedBytes)
                return false;

            _reservedTransferCount++;
            _globalReservedBytes += bytes;
            _reservedBytes = bytes;
            _budgetReserved = true;
            return true;
        }
    }

    private void ReleaseBudget()
    {
        if (!_budgetReserved)
            return;

        lock (BudgetLock)
        {
            _reservedTransferCount = Math.Max(0, _reservedTransferCount - 1);
            _globalReservedBytes = Math.Max(0, _globalReservedBytes - _reservedBytes);
            _reservedBytes = 0;
            _budgetReserved = false;
        }
    }

    private void EndSession(bool completed, string reason, bool stopSend = true)
    {
        if (_state == State.Complete || _state == State.Aborted)
            return;

        _state = completed ? State.Complete : State.Aborted;
        if (stopSend && _sendHandle.IsRunning)
            Timing.KillCoroutines(_sendHandle);
        if (_watchdogHandle.IsRunning)
            Timing.KillCoroutines(_watchdogHandle);
        ReleaseBudget();

        if (ActiveSessions.TryGetValue(_hub, out var current) && ReferenceEquals(current, this))
            ActiveSessions.Remove(_hub);

        if (!completed)
        {
            Logger.Warn(
                $"FileTransfer: Aborted session {_sessionId:x16} for {_hub.LoggedNameFromRefHub()}: {reason}.");
            return;
        }

        Logger.Info(
            $"FileTransfer: Completed session {_sessionId:x16} for {_hub.LoggedNameFromRefHub()}: {reason}.");
        try
        {
            SessionCompleted?.Invoke(_hub);
        }
        catch (Exception ex)
        {
            Logger.Error($"FileTransfer: SessionCompleted handler threw: {ex}");
        }
    }

    private static ulong CreateSessionId()
    {
        var bytes = new byte[sizeof(ulong)];
        using (var rng = RandomNumberGenerator.Create())
        {
            ulong value;
            do
            {
                rng.GetBytes(bytes);
                value = BitConverter.ToUInt64(bytes, 0);
            }
            while (value == 0);
            return value;
        }
    }

    private static bool HashesEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
            return false;

        int difference = 0;
        for (int i = 0; i < left.Length; i++)
            difference |= left[i] ^ right[i];
        return difference == 0;
    }
}
