using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anomaly.Server.Networking;
using Anomaly.Server.Networking.Messages;
using MEC;
using Mirror;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.FileTransfer;

public class FileTransferSession
{
    private static readonly Dictionary<ReferenceHub, FileTransferSession> ActiveSessions =
        new Dictionary<ReferenceHub, FileTransferSession>();

    private readonly ReferenceHub _hub;
    private readonly Queue<ServerAssetManager.AssetInfo> _pendingFiles = new Queue<ServerAssetManager.AssetInfo>();
    private ServerAssetManager.AssetInfo _currentFile;
    private bool _sending;
    private int _retryCount;
    private CoroutineHandle _sendHandle;

    public const int DefaultChunkSize = 16384;
    public const int DefaultChunkIntervalMs = 5;
    public const int MaxRetries = 2;

    private FileTransferSession(ReferenceHub hub)
    {
        _hub = hub;
    }

    public static void StartForPlayer(ReferenceHub hub)
    {
        if (ServerAssetManager.AllAssets.Count == 0)
            return;

        var session = new FileTransferSession(hub);
        ActiveSessions[hub] = session;
        session.SendManifest();
    }

    public static void CleanupPlayer(ReferenceHub hub)
    {
        if (ActiveSessions.TryGetValue(hub, out var session))
        {
            if (session._sendHandle.IsRunning)
                Timing.KillCoroutines(session._sendHandle);
            ActiveSessions.Remove(hub);
        }
    }

    public static void HandleMessage(ReferenceHub sender, IAnomalyMessage msg)
    {
        if (!ActiveSessions.TryGetValue(sender, out var session))
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
        var manifest = ServerAssetManager.BuildManifest();
        AnomalyMessaging.SendToClient(_hub, manifest);
        Logger.Info($"FileTransfer: Sent manifest ({manifest.Files.Count} files) to {_hub.LoggedNameFromRefHub()}");
    }

    private void HandleFileRequest(FileRequestMessage request)
    {
        if (request.RequestedHashes.Count == 0)
        {
            Logger.Info($"FileTransfer: {_hub.LoggedNameFromRefHub()} has all files cached.");
            ActiveSessions.Remove(_hub);
            return;
        }

        foreach (var hash in request.RequestedHashes)
        {
            if (ServerAssetManager.TryGetAsset(hash, out var asset))
                _pendingFiles.Enqueue(asset);
            else
                Logger.Warn($"FileTransfer: Client requested unknown hash {BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()}");
        }

        Logger.Info($"FileTransfer: Sending {_pendingFiles.Count} files to {_hub.LoggedNameFromRefHub()}");
        SendNextFile();
    }

    private void SendNextFile()
    {
        if (_pendingFiles.Count == 0)
        {
            Logger.Info($"FileTransfer: All files sent to {_hub.LoggedNameFromRefHub()}");
            ActiveSessions.Remove(_hub);
            return;
        }

        _currentFile = _pendingFiles.Dequeue();
        _retryCount = 0;
        _sending = true;
        _sendHandle = Timing.RunCoroutine(SendFileChunks());
    }

    private IEnumerator<float> SendFileChunks()
    {
        byte[] fileData;
        try
        {
            fileData = File.ReadAllBytes(_currentFile.FullPath);
        }
        catch (Exception ex)
        {
            Logger.Error($"FileTransfer: Failed to read '{_currentFile.FullPath}': {ex.Message}");
            SendNextFile();
            yield break;
        }

        int chunkSize = DefaultChunkSize;
        int totalChunks = (fileData.Length + chunkSize - 1) / chunkSize;

        for (int i = 0; i < totalChunks; i++)
        {
            int offset = i * chunkSize;
            int length = Math.Min(chunkSize, fileData.Length - offset);
            var chunkData = new byte[length];
            Array.Copy(fileData, offset, chunkData, 0, length);

            var chunkMsg = new FileChunkMessage
            {
                Hash = _currentFile.Hash,
                ChunkIndex = (ushort)i,
                TotalChunks = (ushort)totalChunks,
                Data = chunkData
            };

            if (_hub.connectionToClient == null || !_hub.connectionToClient.isReady)
            {
                Logger.Warn($"FileTransfer: Connection lost during transfer to {_hub.LoggedNameFromRefHub()}");
                CleanupPlayer(_hub);
                yield break;
            }

            AnomalyMessaging.SendToClient(_hub, chunkMsg);

            // Pace sends to avoid burst-flooding
            yield return Timing.WaitForSeconds(DefaultChunkIntervalMs / 1000f);
        }

        // All chunks sent, signal completion
        AnomalyMessaging.SendToClient(_hub, new FileTransferCompleteMessage { Hash = _currentFile.Hash });
        _sending = false;
    }

    private void HandleFileAck(FileAckMessage ack)
    {
        string hex = BitConverter.ToString(ack.Hash).Replace("-", "").ToLowerInvariant();

        if (ack.Status == 0)
        {
            Logger.Info($"FileTransfer: File {hex} acknowledged by {_hub.LoggedNameFromRefHub()}");
            SendNextFile();
        }
        else
        {
            _retryCount++;
            if (_retryCount <= MaxRetries)
            {
                Logger.Warn($"FileTransfer: File {hex} failed, retrying ({_retryCount}/{MaxRetries})");
                _sending = true;
                _sendHandle = Timing.RunCoroutine(SendFileChunks());
            }
            else
            {
                Logger.Error($"FileTransfer: File {hex} failed after {MaxRetries} retries. Skipping.");
                SendNextFile();
            }
        }
    }
}
