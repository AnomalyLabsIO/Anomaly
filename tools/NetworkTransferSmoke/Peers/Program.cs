#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using LiteNetLib;
using LiteNetLib.Utils;
using Mirror;

namespace Anomaly.NetworkTransferPeers;

// Private load fixture. Uses the game's managed transport and Mirror framing;
// it deliberately stops before activation/Ready and never claims gameplay coverage.
internal static class Program
{
    internal static readonly Stopwatch Clock = Stopwatch.StartNew();

    internal static int Run(string[] args, CancellationToken cancellation)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-check")
            {
                var writer = new NetworkWriter();
                writer.WriteInt(123);
                var batch = new NetworkWriter();
                batch.WriteDouble(1.0);
                Compression.CompressVarUInt(batch, (ulong)writer.Position);
                batch.WriteBytes(writer.buffer, 0, writer.Position);
                var input = new NetworkReader(batch.ToArraySegment());
                if (input.ReadDouble() != 1.0 || Compression.DecompressVarUInt(input) != 4 || input.ReadInt() != 123 || input.Remaining != 0)
                    throw new InvalidOperationException("Owned Mirror framing roundtrip failed.");
                var listener = new EventBasedNetListener();
                var host = new NetManager(listener) { IPv6Enabled = false, UseNativeSockets = false, ChannelsCount = 64 };
                try
                {
                    if (!host.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0)) throw new InvalidOperationException("LNL start failed.");
                    host.PollEvents();
                    Console.WriteLine("Actual managed LiteNetLib start/stop and owned Mirror framing roundtrip passed.");
                }
                finally { host.Stop(); }
                return 0;
            }
            if (args.Length != 5)
                throw new ArgumentException("Usage: <private-server-identity.yml> <expected-content.json> <client-artifact.dll> <new-evidence-directory> <peer-count:1..3>");
            string evidence = Path.GetFullPath(args[3]);
            string? repository = Path.GetDirectoryName(evidence);
            while (repository != null && !File.Exists(Path.Combine(repository, "Anomaly.slnx"))) repository = Path.GetDirectoryName(repository);
            if (repository == null) throw new ArgumentException("Evidence is not inside the repository.");
            string artifacts = Path.Combine(repository, "artifacts") + Path.DirectorySeparatorChar;
            if (!evidence.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || Directory.Exists(evidence))
                throw new ArgumentException("Use a new evidence directory under repository artifacts.");
            string identityPath = Path.GetFullPath(args[0]);
            if (!identityPath.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Pin only the owned private-server identity under artifacts.");
            int count = int.Parse(args[4]);
            if (count < 1 || count > 3) throw new ArgumentOutOfRangeException(nameof(count));
            var scenario = PeerReceivePolicy.ScenarioOptions.Parse(
                Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_PEER_CANCEL_AFTER_BYTES")!,
                Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_PEER_HOLD_FIRST_BULK")!);
            string serverHost = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_PEER_HOST") ?? "127.0.0.1";
            if (serverHost != "127.0.0.1" && serverHost != "127.0.0.2")
                throw new ArgumentException("Peer host must be 127.0.0.1 or the private relay at 127.0.0.2; port is fixed at 29149.");
            // This synthetic protocol implementation makes the required core
            // version claim, while explicitly reporting incomplete protection
            // coverage. It does not claim a running MelonLoader client.
            string coreVersion = AssemblyName.GetAssemblyName(Path.GetFullPath(args[2])).Version!.ToString();
            string pin = ReadServerPin(identityPath);
            using var expected = JsonDocument.Parse(File.ReadAllBytes(args[1]));
            Directory.CreateDirectory(evidence);
            double cohortStarted = Clock.Elapsed.TotalSeconds;
            File.WriteAllText(Path.Combine(evidence, "inputs.json"), JsonSerializer.Serialize(new {
                endpoint = serverHost + ":29149", peerCount = count, serverFingerprint = pin,
                expectedSha256 = Hex(Hash(File.ReadAllBytes(args[1]))),
                coreVersionClaim = coreVersion, referencedCoreSha256 = Hex(Hash(File.ReadAllBytes(args[2]))),
                coreAssemblyLoaded = false, melonLoaderInitialized = false,
                cancelFirstPeerAfterBytes = scenario.CancelAfterBytes, holdFirstBulkChunk = scenario.HoldFirstBulkChunk,
                scenarioPeerIndex = 0,
                dispatchHoldScope = "One original real-wire chunk held before application replay acceptance/disk/credit. Application dispatch reordering, not packet loss.",
                audit = "CoverageIncomplete", requiresAllowRiskyClientModifications = true,
                limitation = "Protocol acquisition peers; no Unity, script activation, Ready/AddPlayer or voice qualification."
            }));
            using var offers = new CountdownEvent(count);
            byte[]? reconnectIdentity = scenario.CancelAfterBytes > 0 ? AuthCrypto.CreateIdentity() : null;
            FixturePeer[] peers;
            FixturePeer? replacement = null;
            try
            {
                peers = Enumerable.Range(0, count).Select(index => new FixturePeer(index, serverHost, pin, coreVersion,
                    expected.RootElement, Path.Combine(evidence, "peer-" + index), offers, cancellation,
                    index == 0 ? scenario : new PeerReceivePolicy.ScenarioOptions(), index == 0 ? reconnectIdentity : null)).ToArray();
                Task.WaitAll(peers.Select((peer, index) => Task.Run(() =>
                {
                    peer.Run();
                    if (index != 0 || !peer.ExpectedCancellation || reconnectIdentity == null) return;
                    using var replacementOffer = new CountdownEvent(1);
                    replacement = new FixturePeer(0, serverHost, pin, coreVersion, expected.RootElement,
                        Path.Combine(evidence, "peer-0-rejoin"), replacementOffer, cancellation,
                        new PeerReceivePolicy.ScenarioOptions(), reconnectIdentity);
                    replacement.Run();
                })).ToArray());
            }
            finally { if (reconnectIdentity != null) Array.Clear(reconnectIdentity, 0, reconnectIdentity.Length); }
            int expectedCancellations = scenario.CancelAfterBytes > 0 ? 1 : 0;
            bool replacementValid = expectedCancellations == 0 || replacement != null && replacement.Success &&
                replacement.IdentityFingerprint == peers[0].IdentityFingerprint && replacement.ContentSessionId != peers[0].ContentSessionId &&
                replacement.CryptoFingerprint != peers[0].CryptoFingerprint;
            bool success = peers.All(peer => peer.Success || peer.ExpectedCancellation) && replacementValid &&
                PeerCoordinator.AllSessionsCompleted(count, expectedCancellations);
            File.WriteAllText(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new {
                success, completedPeers = peers.Count(peer => peer.Success) + (replacement?.Success == true ? 1 : 0),
                expectedCancelledPeers = expectedCancellations, cancelledPeers = peers.Count(peer => peer.ExpectedCancellation),
                peers = peers.Select(peer => peer.Summary()), replacement = replacement?.Summary(),
                rejoinPerformed = replacement != null, sameIdentityFreshSession = expectedCancellations == 0 ? (bool?)null : replacementValid,
                peerAttemptCount = peers.Length + (replacement == null ? 0 : 1), cohortWallSeconds = Clock.Elapsed.TotalSeconds - cohortStarted,
                totalAttemptReceivedBytes = peers.Sum(peer => peer.ReceivedBytes) + (replacement?.ReceivedBytes ?? 0),
                scope = "Actual managed transport, signed authentication, exact pinned cold acquisition and verified ACKs only. Never sends activation or Ready."
            }));
            return success ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static string ReadServerPin(string file)
    {
        string? line = File.ReadLines(file).FirstOrDefault(value => value.TrimStart().StartsWith("identity_base64:", StringComparison.Ordinal));
        if (line == null) throw new InvalidDataException("Private server identity is missing identity_base64.");
        byte[] identity = Convert.FromBase64String(line.Substring(line.IndexOf(':') + 1).Trim().Trim('\'', '"'));
        try
        {
            if (identity.Length != 97 || identity[32] != 4) throw new InvalidDataException("Server identity format mismatch.");
            var publicKey = new byte[65];
            Array.Copy(identity, 32, publicKey, 0, publicKey.Length);
            return Hex(Hash(publicKey));
        }
        finally { Array.Clear(identity, 0, identity.Length); }
    }

    internal static byte[] Hash(byte[] value)
    {
        using var sha = SHA256.Create(); return sha.ComputeHash(value);
    }
    internal static byte[] Hash(Stream value) { using var sha = SHA256.Create(); return sha.ComputeHash(value); }
    internal static string Hex(byte[] value) => BitConverter.ToString(value).Replace("-", "");
    internal static int Domains(ulong value) { int count = 0; while (value != 0) { value &= value - 1; count++; } return count; }
}

internal sealed class FixturePeer
{
    private const int ChunkBytes = 16 * 1024;
    private readonly int _index;
    private readonly string _pin, _output, _serverHost;
    private readonly JsonElement _expected;
    private readonly CountdownEvent _offers;
    private readonly CancellationToken _cancellation;
    private readonly PeerReceivePolicy.ScenarioOptions _scenario;
    private readonly ClientAuthSession _auth;
    private readonly NetManager _host;
    private readonly AnomalyReplayState _replay = new();
    private readonly PeerReceivePolicy _receivePolicy = new();
    private readonly AnomalyReplayState[] _bulkReplay = Enumerable.Range(0, 16).Select(_ => new AnomalyReplayState()).ToArray();
    private readonly Dictionary<string, ReceivedFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, long> _laneBytes = new();
    private readonly uint[] _releasedFrames = new uint[16];
    private readonly ushort _carrierId;
    private readonly string _displayName;
    private NetPeer? _peer;
    private ServerHandshake? _handshake;
    private string? _serverIdentifier;
    private PasswordChallenge? _challenge;
    private AuthSessionKeys? _keys;
    private byte[]? _cookie;
    private ContentCatalog? _catalog;
    private FileManifestMessage? _manifest;
    private bool _requestSent, _offered, _offerBarrierReleased, _reconnect, _creditDirty, _acquired, _transportConnected;
    private double _preauthDeadline, _loginAt;
    private double _reconnectAt, _requestAt, _firstByteAt, _finishedAt, _lastCreditAt, _lastSampleAt;
    private double _queuedAt, _admittedAt;
    private int _queuedPosition;
    private ushort _queueWaitMilliseconds;
    private uint _creditSequence;
    private ulong _releasedBytes, _laneMask;
    private string? _error;
    private long _receivedBytes;
    private int _pendingAckDeliveries;
    private double _lastAckDelivery;
    private PeerCoordinator.ServerCompletionProof? _serverCompletion;
    private PeerCoordinator.ServerCleanupProof? _serverCleanup;
    private PeerCoordinator.ServerHoleProof? _serverHole;
    private FileChunkMessage? _heldChunk;
    private byte _heldLane;
    private uint _heldCounter;
    private MessageChannel _heldChannel;
    private double _heldAt, _cancelAt;
    private bool _holeReleased, _cancelRequested, _disconnectObserved;
    private long _postCancelIgnoredBytes;
    internal string IdentityFingerprint { get; }
    internal string? CryptoFingerprint { get; private set; }
    internal ulong ContentSessionId => _manifest?.SessionId ?? 0;
    internal bool ExpectedCancellation { get; private set; }
    internal long ReceivedBytes => _receivedBytes;

    internal FixturePeer(int index, string serverHost, string pin, string coreVersion, JsonElement expected, string output,
        CountdownEvent offers, CancellationToken cancellation, PeerReceivePolicy.ScenarioOptions scenario, byte[]? sharedIdentity = null)
    {
        _index = index; _pin = pin; _expected = expected; _output = output; _offers = offers; _cancellation = cancellation;
        _serverHost = serverHost;
        _scenario = scenario;
        _displayName = "Protocol fixture " + index;
        Directory.CreateDirectory(output);
        byte[] identity = sharedIdentity ?? AuthCrypto.CreateIdentity();
        try
        {
            _auth = ClientAuthSession.Generate(identity);
            IdentityFingerprint = Program.Hex(Program.Hash(identity.Skip(32).Take(65).ToArray()));
        }
        finally { if (sharedIdentity == null) Array.Clear(identity, 0, identity.Length); }
        _auth.BindConnectionContext(GameCore.Version.Major, GameCore.Version.Minor, GameCore.Version.Revision,
            GameCore.Version.BackwardCompatibility, GameCore.Version.BackwardCompatibility ? GameCore.Version.BackwardRevision : (byte)0,
            serverHost + ":29149");
        _auth.BindAdmission(new ClientModInventory(new[] {
            new ClientModEntry(ClientModInventory.CoreId, coreVersion), new ClientModEntry("anomaly.protocol-test", "1.0.0")
        }), new ClientIntegrityAudit(ClientIntegrityAuditFlags.CoverageIncomplete, false));
        var listener = new EventBasedNetListener();
        listener.PeerConnectedEvent += peer =>
        {
            if (_keys == null) { Fail("Transport accepted before signed fixture login."); return; }
            _peer = peer; _transportConnected = true; Record("connected", new { peer.Id });
        };
        listener.PeerDisconnectedEvent += Disconnected;
        listener.NetworkErrorEvent += (_, error) => Fail("Network socket error: " + error);
        listener.NetworkReceiveEvent += Receive;
        listener.ConnectionRequestEvent += request => request.Reject();
        listener.DeliveryEvent += (_, token) =>
        {
            if (token is not AckDelivery) return;
            _pendingAckDeliveries--;
            _lastAckDelivery = Program.Clock.Elapsed.TotalSeconds;
        };
        _host = new NetManager(listener) { IPv6Enabled = false, UseNativeSockets = false,
            ChannelsCount = 64, UpdateTime = 5, EnableStatistics = true, DisconnectTimeout = 10000, AutoRecycle = true };
        var carrier = typeof(NetManager).Assembly.GetType("EncryptedChannelManager+EncryptedMessageOutside", true)!;
        _carrierId = unchecked((ushort)carrier.FullName!.GetStableHashCode());
    }

    internal bool Success { get; private set; }

    internal void Run()
    {
        try
        {
            if (!_host.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0)) throw new InvalidOperationException("Cannot bind private peer.");
            _preauthDeadline = Program.Clock.Elapsed.TotalSeconds + 30;
            Connect();
            double started = Program.Clock.Elapsed.TotalSeconds;
            while (_error == null && !Success && !ExpectedCancellation && Program.Clock.Elapsed.TotalSeconds - started < 180 && !_cancellation.IsCancellationRequested)
            {
                _host.PollEvents();
                if (_error != null) break;
                double now = Program.Clock.Elapsed.TotalSeconds;
                if (!_transportConnected && now >= _preauthDeadline)
                    throw new TimeoutException("Fixture's fixed 30-second preauthentication deadline expired.");
                _receivePolicy.Check(now);
                if (_reconnect && now >= _reconnectAt) { _reconnect = false; Connect(); }
                if (_offered && !_requestSent && _offers.CurrentCount == 0 &&
                    (_scenario.CancelAfterBytes == 0 || PeerCoordinator.HasObservedOwner(ContentSessionId))) RequestFiles();
                if (!_cancelRequested && _scenario.CancelAfterBytes > 0 && _receivedBytes >= _scenario.CancelAfterBytes)
                {
                    _cancelRequested = true; _cancelAt = now;
                    PeerCoordinator.ExpectCancellation(ContentSessionId);
                    Record("expected-cancellation-requested", new { session = ContentSessionId, receivedBytes = _receivedBytes,
                        cancelThresholdBytes = _scenario.CancelAfterBytes, contentDeadlineRenewed = false });
                    _peer!.Disconnect(); // Exercise the actual normal disconnect cleanup, without a synthetic file message.
                }
                if (_cancelRequested)
                {
                    if (PeerCoordinator.ObservationError is string cleanupError) throw new InvalidOperationException(cleanupError);
                    if (now - _cancelAt > 10) throw new TimeoutException("Cancelled peer owner was not observed clean within ten seconds.");
                    if (_disconnectObserved && PeerCoordinator.TryGetCleanup(ContentSessionId, out var cleanup))
                    { _serverCleanup = cleanup; ExpectedCancellation = true; }
                }
                if (_heldChunk != null)
                {
                    if (PeerCoordinator.ObservationError is string holeError) throw new InvalidOperationException(holeError);
                    if (now - _heldAt > 10) throw new TimeoutException("The original held chunk did not produce an observable blocked eight-counter span.");
                    if (PeerCoordinator.TryGetHole(ContentSessionId, out var hole))
                    {
                        _serverHole = hole;
                        if (_bulkReplay[_heldChunk.Domain].TryAcceptRx(_heldChannel, _heldCounter, out _) != AnomalyReplayState.RxResult.Accepted)
                            throw new InvalidDataException("The delayed original chunk was rejected by replay protection.");
                        var held = _heldChunk; _heldChunk = null; _holeReleased = true;
                        ReceiveChunk(held, _heldLane, _heldCounter, true);
                        Record("dispatch-hole-released", new { session = ContentSessionId, domain = held.Domain, counter = _heldCounter,
                            holdSeconds = now - _heldAt, scope = "Application dispatch reordering, not packet loss", proof = hole.Report() });
                    }
                }
                if (!_cancelRequested && _creditDirty && now - _lastCreditAt >= .025) FlushCredit();
                if (_acquired && _pendingAckDeliveries == 0)
                {
                    if (PeerCoordinator.ObservationError is string observationError)
                        throw new InvalidOperationException("Server completion observer failed: " + observationError);
                    if (PeerCoordinator.TryGetCompletion(_manifest!.SessionId, out var proof))
                    {
                        _serverCompletion = proof;
                        Success = true;
                    }
                }
                if (_requestSent && now - _lastSampleAt >= .25)
                {
                    _lastSampleAt = now;
                    Record("sample", new { receivedBytes = _receivedBytes, releasedBytes = _releasedBytes,
                        phase = _receivePolicy.State.ToString(), outstandingBytes = _receivePolicy.OutstandingBytes,
                        outstandingFrames = _receivePolicy.OutstandingFrames, queuedPosition = _queuedPosition,
                        pingHalfRttMs = _peer?.Ping, roundTripMs = _peer == null ? (int?)null : _peer.Ping * 2, mtu = _peer?.Mtu,
                        consumedCounters = _receivePolicy.ConsumedCounters, releasedFrames = _releasedFrames,
                        bytesReceived = _peer?.Statistics.BytesReceived, packetsReceived = _peer?.Statistics.PacketsReceived });
                }
                Thread.Sleep(1);
            }
            if (!Success && !ExpectedCancellation && _error == null) Fail("Protocol peer deadline elapsed.");
        }
        catch (Exception error) { Fail(error.ToString()); }
        finally
        {
            foreach (var file in _files.Values) file.Dispose();
            _peer?.Disconnect();
            _host.Stop();
            _keys?.Dispose();
            File.WriteAllText(Path.Combine(_output, "result.json"), JsonSerializer.Serialize(Summary()));
        }
    }

    internal object Summary() => new { index = _index, success = Success, error = _error,
        outcome = ExpectedCancellation ? "ExpectedCancelled" : Success ? "Completed" : "Failed",
        expectedCancellation = ExpectedCancellation, cancelRequestedSeconds = _cancelAt, disconnectObserved = _disconnectObserved,
        postCancelIgnoredTransportBytes = _postCancelIgnoredBytes, serverCleanup = _serverCleanup?.Report(),
        identityFingerprint = IdentityFingerprint, cryptoSessionFingerprint = CryptoFingerprint, sessionId = ContentSessionId,
        dispatchHoleRequested = _scenario.HoldFirstBulkChunk, dispatchHoleReleased = _holeReleased, serverHole = _serverHole?.Report(),
        requestedBytes = _manifest?.Files.Sum(file => (long)file.Size), receivedBytes = _receivedBytes,
        loginSeconds = _loginAt, originalContentDeadlineSeconds = _loginAt == 0 ? 0 : _loginAt + 120,
        contentDeadlineRenewed = false,
        requestSeconds = _requestAt, firstByteSeconds = _firstByteAt, finishedSeconds = _finishedAt,
        queueSeconds = _queuedAt == 0 || _admittedAt == 0 ? 0 : _admittedAt - _queuedAt,
        advertisedQueueWaitMilliseconds = _queueWaitMilliseconds,
        transferSeconds = _finishedAt == 0 ? 0 : _finishedAt - _requestAt, laneMask = _laneMask,
        laneBytes = _laneBytes, verifiedFiles = _files.Values.Count(file => file.Acked),
        peakOutstandingBytes = _receivePolicy.PeakOutstandingBytes, peakOutstandingFrames = _receivePolicy.PeakOutstandingFrames,
        peakDomainOutstandingFrames = _receivePolicy.PeakDomainOutstandingFrames, peakActiveReceiveFiles = _receivePolicy.PeakActiveFiles,
        peakCounterSpan = _receivePolicy.PeakCounterSpan, bulkProfile = _manifest?.BulkProfileVersion,
        counterBases = _manifest?.BulkCounterBases, consumedCounters = _receivePolicy.ConsumedCounters,
        lastTransportAckSeconds = _lastAckDelivery,
        serverCompletion = _serverCompletion?.Report(),
        activated = false, nativeReadySent = false };

    private void Connect()
    {
        var writer = new NetDataWriter();
        bool login = _handshake != null;
        writer.Put((byte)(login ? 4 : 3));
        writer.Put(GameCore.Version.Major); writer.Put(GameCore.Version.Minor); writer.Put(GameCore.Version.Revision);
        writer.Put(GameCore.Version.BackwardCompatibility);
        if (GameCore.Version.BackwardCompatibility) writer.Put(GameCore.Version.BackwardRevision);
        writer.Put(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30);
        if (!login) writer.PutBytesWithLength(_auth.CreateHandshake(_cookie).Encode());
        else
        {
            writer.PutBytesWithLength(_auth.CreateLoginAttempt(_handshake!, _serverIdentifier!, _challenge!, _displayName, null).Encode());
            writer.Put(_serverIdentifier!); writer.Put(_displayName);
            if (_keys == null)
            {
                _keys = _auth.DeriveSessionKeys(_handshake!, _serverIdentifier!, _challenge!);
                CryptoFingerprint = Program.Hex(Program.Hash(_keys.SessionId));
                _loginAt = Program.Clock.Elapsed.TotalSeconds;
                _receivePolicy.LoginSent(_loginAt);
            }
        }
        _peer = _host.Connect(_serverHost, 29149, writer);
        Record("connect-attempt", new { phase = login ? 2 : 1 });
    }

    private void Disconnected(NetPeer peer, DisconnectInfo info)
    {
        if (Success || _error != null) return;
        if (_cancelRequested)
        {
            _disconnectObserved = true;
            Record("expected-disconnect", new { reason = info.Reason.ToString(), session = ContentSessionId });
            return;
        }
        try
        {
            if (info.Reason != DisconnectReason.ConnectionRejected) throw new IOException("Peer disconnected: " + info.Reason);
            byte code = info.AdditionalData.GetByte();
            if (code == 17) // Native server wake/startup delay, not an auth failure.
            {
                byte delaySeconds = info.AdditionalData.GetByte();
                if (!info.AdditionalData.EndOfData) throw new InvalidDataException("Unexpected trailing server delay data.");
                _reconnectAt = PeerReceivePolicy.BoundPreauthRetry(Program.Clock.Elapsed.TotalSeconds, _preauthDeadline, delaySeconds);
                _reconnect = true;
                Record("server-delay", new { delaySeconds, retryAtSeconds = _reconnectAt });
                return;
            }
            if (code == 101 && _handshake == null)
            {
                _cookie = info.AdditionalData.GetBytesWithLength();
                if (_cookie.Length != ClientHandshake.RetryCookieSize) throw new InvalidDataException("Retry cookie size mismatch.");
            }
            else if (code == 100 && _handshake == null)
            {
                var handshake = ServerHandshake.Decode(info.AdditionalData.GetBytesWithLength());
                string identifier = info.AdditionalData.GetString();
                var challenge = PasswordChallenge.NotRequired();
                if (!info.AdditionalData.EndOfData && info.AdditionalData.GetBool())
                    throw new InvalidDataException("Password-protected fixture server unsupported.");
                if (!handshake.GetIdentityFingerprint().Equals(_pin, StringComparison.OrdinalIgnoreCase) ||
                    !_auth.ValidateServerHandshake(handshake, identifier, challenge))
                    throw new CryptographicException("Pinned signed server transcript mismatch.");
                _handshake = handshake; _serverIdentifier = identifier; _challenge = challenge;
            }
            else throw new InvalidDataException("Server rejected protocol fixture with code " + code);
            _reconnectAt = Program.Clock.Elapsed.TotalSeconds + .75; _reconnect = true;
        }
        catch (Exception error) { Fail(error.ToString()); }
    }

    private void Receive(NetPeer peer, NetPacketReader packet, byte lane, DeliveryMethod method)
    {
        try
        {
            if (_cancelRequested) { _postCancelIgnoredBytes += packet.AvailableBytes; return; }
            var segment = packet.GetRemainingBytesSegment();
            var batch = new NetworkReader(segment);
            if (batch.Remaining < 8) throw new InvalidDataException("Invalid Mirror batch.");
            batch.ReadDouble();
            while (batch.Remaining != 0)
            {
                ulong encodedSize = Compression.DecompressVarUInt(batch);
                if (encodedSize < 2 || encodedSize > (ulong)batch.Remaining)
                    throw new InvalidDataException("Invalid framed Mirror message size.");
                var message = batch.ReadBytesSegment((int)encodedSize);
                var reader = new NetworkReader(message);
                if (reader.ReadUShort() != _carrierId) continue; // Scene/seed/control are not readiness acknowledgements.
                if (reader.ReadByte() != 1) throw new InvalidDataException("Unsecured fixture content.");
                int length = reader.ReadInt();
                if (length < AuthTransportCrypto.EnvelopeOverhead || length > AnomalyChannel.MaxEncryptedEnvelopeBytes || reader.Remaining != length)
                    throw new InvalidDataException("Carrier bounds mismatch.");
                var encrypted = reader.ReadBytesSegment(length).ToArray();
                if (_keys == null) throw new InvalidDataException("Content before authenticated session.");
                byte[] plain = AuthTransportCrypto.Decrypt(_keys.ServerToClientKey, _keys.SessionId, _keys.TranscriptHash,
                    AuthTransportDirection.ServerToClient, encrypted);
                if (plain.Length < 9 || plain.Length > AnomalyChannel.MaxEnvelopeBytes || plain[0] != 0xFE)
                    throw new InvalidDataException("Invalid authenticated application envelope.");
                uint counter = BitConverter.ToUInt32(plain, 1), hash = BitConverter.ToUInt32(plain, 5);
                if (!AnomalyMessageRegistry.TryCreate(hash, out var decoded) || !AnomalyMessageRegistry.TryGetChannel(hash, out var channel))
                    throw new InvalidDataException("Unknown authenticated application message.");
                using var stream = new MemoryStream(plain, 9, plain.Length - 9, false);
                using var binary = new BinaryReader(stream);
                decoded.Deserialize(binary);
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing application payload.");
                if (_scenario.HoldFirstBulkChunk && !_holeReleased && _heldChunk == null && decoded is FileChunkMessage original &&
                    original.Domain == 0 && counter == _manifest!.BulkCounterBases[0] + 1)
                {
                    BeginChunkReceipt(original, lane, counter);
                    _heldChunk = original; _heldLane = lane; _heldCounter = counter; _heldChannel = channel;
                    _heldAt = Program.Clock.Elapsed.TotalSeconds;
                    PeerCoordinator.ExpectHole(ContentSessionId, original.Domain, _manifest.BulkCounterBases[original.Domain]);
                    Record("dispatch-hole-held", new { session = ContentSessionId, domain = original.Domain, counter,
                        bytes = original.Data.Length, replayAccepted = false, diskWritten = false, credited = false });
                    continue;
                }
                var replay = decoded is FileChunkMessage chunk ? _bulkReplay[chunk.Domain] : _replay;
                if (replay.TryAcceptRx(channel, counter, out _) != AnomalyReplayState.RxResult.Accepted)
                    throw new InvalidDataException("Application replay rejected.");
                Handle(decoded, lane, counter);
            }
        }
        catch (Exception error) { Fail(error.ToString()); }
    }

    private void Handle(IAnomalyMessage message, byte lane, uint counter)
    {
        if (message is ContentCatalogMessage catalog) { _catalog = catalog.Catalog; CheckOffer(); }
        else if (message is FileManifestMessage manifest) { _manifest = manifest; CheckOffer(); }
        else if (message is FileChunkMessage chunk) ReceiveChunk(chunk, lane, counter);
        else if (message is FileTransferCompleteMessage complete)
        {
            var file = FileFor(complete.SessionId, complete.Hash, complete.Attempt);
            if (file.Acked || file.CompleteSeen) return;
            _receivePolicy.FirstCompletion(Program.Clock.Elapsed.TotalSeconds);
            file.CompleteSeen = true; FinishFile(file);
        }
        else if (message.MessageName == "anomaly:filestatus")
        {
            var type = message.GetType();
            if ((ulong)type.GetProperty("SessionId")!.GetValue(message)! != _manifest?.SessionId)
                throw new InvalidDataException("Status session mismatch.");
            string status = type.GetProperty("Status")!.GetValue(message)!.ToString()!;
            double now = Program.Clock.Elapsed.TotalSeconds;
            ushort wait = Convert.ToUInt16(type.GetProperty("QueueWaitMilliseconds")!.GetValue(message));
            byte position = Convert.ToByte(type.GetProperty("QueuePosition")!.GetValue(message));
            if (status == "Queued")
            {
                var prior = _receivePolicy.State;
                _receivePolicy.Queued(now, wait, position);
                if (prior == PeerReceivePolicy.Phase.AwaitingAdmission)
                { _queuedAt = now; _queuedPosition = position; _queueWaitMilliseconds = wait; }
            }
            else if (status == "Admitted")
            {
                if (wait != 0 || position != 0) throw new InvalidDataException("Invalid admitted status fields.");
                _receivePolicy.Admitted(now);
                if (_admittedAt == 0) _admittedAt = now;
                _queuedPosition = 0;
            }
            else throw new InvalidDataException("Unknown admission status.");
            Record("admission", new { status, queuePosition = _queuedPosition, advertisedWaitMilliseconds = wait,
                phase = _receivePolicy.State.ToString() });
        }
    }

    private void CheckOffer()
    {
        if (_offered || _catalog == null || _manifest == null) return;
        if (!_catalog.ManifestSha256.SequenceEqual(_manifest.ManifestSha256)) throw new InvalidDataException("Catalog/manifest mismatch.");
        var pins = _expected.GetProperty("ManifestFiles").EnumerateArray().ToDictionary(pin => pin.GetProperty("hash").GetString()!, pin => pin.GetProperty("bytes").GetInt64(), StringComparer.OrdinalIgnoreCase);
        if (_manifest.Files.Count != pins.Count || _manifest.Files.Any(file => !pins.TryGetValue(Program.Hex(file.Hash), out long size) || size != file.Size))
            throw new InvalidDataException("Offered inventory differs from exact generated pins.");
        var packs = _expected.GetProperty("AssetPacks").EnumerateArray().ToArray();
        if (_catalog.Packs.Count != packs.Length) throw new InvalidDataException("Asset pack count differs from pins.");
        foreach (var pack in packs)
        {
            string id = pack.GetProperty("PackId").GetString()!;
            var actual = _catalog.Packs.Single(value => value.PackId == id);
            if (!Program.Hex(actual.BundleHash).Equals(pack.GetProperty("BundleSha256").GetString(), StringComparison.OrdinalIgnoreCase) ||
                !Program.Hex(actual.IndexHash).Equals(pack.GetProperty("IndexSha256").GetString(), StringComparison.OrdinalIgnoreCase) ||
                actual.DecodedBytes != pack.GetProperty("DecodedBytes").GetInt64())
                throw new InvalidDataException("Catalog asset pins mismatch.");
        }
        string source = _expected.GetProperty("ScriptSha256").GetString()!;
        if (_catalog.ScriptPacks.Count != 1 || _catalog.ScriptPacks[0].Sources.Count != 1 ||
            _catalog.ScriptPacks[0].PackId != _expected.GetProperty("ScriptPackId").GetString() ||
            !Program.Hex(_catalog.ScriptPacks[0].Sources[0].Hash).Equals(source, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Benign script pins mismatch.");
        _laneMask = _manifest.BulkLaneMask;
        if (_manifest.BulkProfileVersion != 2 || _laneMask == 0 || (_laneMask & 63) != 0 || Program.Domains(_laneMask) > 16 ||
            _manifest.BulkCounterBases == null || _manifest.BulkCounterBases.Length != 16)
            throw new InvalidDataException("No valid owned bulk pool.");
        foreach (var file in _manifest.Files) _files.Add(Program.Hex(file.Hash), new ReceivedFile(file, _output));
        long requestedBytes = _manifest.Files.Sum(file => (long)file.Size);
        if (_scenario.CancelAfterBytes >= requestedBytes)
            throw new InvalidDataException("Cancellation threshold must be smaller than the pinned cold request.");
        PeerCoordinator.ExpectSession(_manifest.SessionId, _manifest.Files.Count, requestedBytes, _scenario.CancelAfterBytes > 0);
        _offered = true; ReleaseOfferBarrier(); Record("pinned-offer", new { files = pins.Count, bytes = pins.Values.Sum() });
    }

    private void RequestFiles()
    {
        string delayText = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_PEER_REQUEST_DELAY_SECONDS");
        if (!string.IsNullOrEmpty(delayText))
        {
            if (!double.TryParse(delayText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out double delay) || double.IsNaN(delay) || double.IsInfinity(delay) || delay < 0 || delay > 100)
                throw new InvalidDataException("Private consent delay must be between zero and100 seconds.");
            // Simulate a user considering consent. Keep the original signed-login
            // deadline and every transport timer; no server state is changed.
            if (Program.Clock.Elapsed.TotalSeconds - _loginAt < delay) return;
        }
        _receivePolicy.RequestSent(Program.Clock.Elapsed.TotalSeconds, Program.Domains(_laneMask), _manifest!.BulkCounterBases);
        Send(new ContentCatalogDecisionMessage { CatalogSha256 = _catalog!.CatalogSha256,
            ManifestSha256 = _manifest!.ManifestSha256, Decision = ContentCatalogDecision.Approved });
        Send(new FileRequestMessage { SessionId = _manifest.SessionId, RequestedHashes = _manifest.Files.Select(file => file.Hash).ToList(),
            BulkProfileVersion = 2, BulkLaneMask = _laneMask, ReceiveWindowBytes = 2 * 1024 * 1024, ReceiveWindowFrames = 128 });
        _requestAt = Program.Clock.Elapsed.TotalSeconds; _requestSent = true; Record("request", new { session = _manifest.SessionId });
    }

    private ReceivedFile FileFor(ulong session, byte[] hash, byte attempt)
    {
        if (!_requestSent || session != _manifest!.SessionId || attempt != 0 || !_files.TryGetValue(Program.Hex(hash), out var file))
            throw new InvalidDataException("Unexpected transfer/session/attempt.");
        if (!file.Acked) _receivePolicy.BeginFile(Program.Hex(hash));
        return file;
    }

    private ReceivedFile BeginChunkReceipt(FileChunkMessage chunk, byte lane, uint counter)
    {
        var file = FileFor(chunk.SessionId, chunk.Hash, chunk.Attempt);
        int domainCount = Program.Domains(_laneMask);
        if (chunk.Domain >= domainCount || chunk.ProfileVersion != 2) throw new InvalidDataException("Invalid bulk domain.");
        int physical = Enumerable.Range(6, 58).Where(index => (_laneMask & (1UL << index)) != 0).ElementAt(chunk.Domain);
        if (physical != lane) throw new InvalidDataException("Actual physical lane differs from negotiated domain.");
        if (chunk.TotalChunks != file.Chunks.Length || chunk.ChunkIndex >= file.Chunks.Length ||
            chunk.Data.Length != Math.Min(ChunkBytes, file.Entry.Size - (long)chunk.ChunkIndex * ChunkBytes) || file.Chunks[chunk.ChunkIndex])
            throw new InvalidDataException("Duplicate or invalid chunk.");
        _receivePolicy.Chunk(Program.Clock.Elapsed.TotalSeconds, chunk.Domain, chunk.Data.Length, counter);
        _receivedBytes += chunk.Data.Length;
        _laneBytes.TryGetValue(lane, out long prior); _laneBytes[lane] = prior + chunk.Data.Length;
        if (_firstByteAt == 0) _firstByteAt = Program.Clock.Elapsed.TotalSeconds;
        if (_admittedAt == 0) _admittedAt = Program.Clock.Elapsed.TotalSeconds;
        return file;
    }

    private void ReceiveChunk(FileChunkMessage chunk, byte lane, uint counter, bool alreadyReceived = false)
    {
        var file = alreadyReceived ? FileFor(chunk.SessionId, chunk.Hash, chunk.Attempt) : BeginChunkReceipt(chunk, lane, counter);
        file.Open();
        file.Stream!.Position = (long)chunk.ChunkIndex * ChunkBytes;
        file.Stream.Write(chunk.Data, 0, chunk.Data.Length); // Credits follow actual disk writes, not packet arrival.
        _receivePolicy.Check(Program.Clock.Elapsed.TotalSeconds);
        _receivePolicy.DiskWritten(chunk.Domain, counter, chunk.Data.Length);
        file.Chunks[chunk.ChunkIndex] = true; file.Written++;
        _releasedBytes += (ulong)chunk.Data.Length; _releasedFrames[chunk.Domain]++;
        _creditDirty = true;
        FinishFile(file);
    }

    private void FinishFile(ReceivedFile file)
    {
        if (file.Acked || !file.CompleteSeen || file.Written != file.Chunks.Length) return;
        file.Stream!.Flush(true); file.Stream.Dispose(); file.Stream = null;
        using var input = File.OpenRead(file.Path);
        if (input.Length != file.Entry.Size || !Program.Hash(input).SequenceEqual(file.Entry.Hash))
            throw new InvalidDataException("Final size/hash mismatch.");
        _receivePolicy.Check(Program.Clock.Elapsed.TotalSeconds);
        FlushCredit();
        Send(new FileAckMessage { SessionId = _manifest!.SessionId, Hash = file.Entry.Hash, Attempt = 0, Status = 0 });
        _receivePolicy.FileAcknowledged(Program.Hex(file.Entry.Hash));
        file.Acked = true; Record("verified-ack", new { hash = Program.Hex(file.Entry.Hash), bytes = file.Entry.Size });
        if (_files.Values.All(candidate => candidate.Acked))
        {
            if (_scenario.HoldFirstBulkChunk && !_holeReleased)
                throw new InvalidOperationException("Requested dispatch-hole qualification never observed and released an original chunk.");
            _finishedAt = Program.Clock.Elapsed.TotalSeconds;
            _receivePolicy.Acquired(_finishedAt);
            // Wait for delivery events and the independently observed production
            // server Complete state; an elapsed grace period is not evidence.
            _acquired = true;
            Record("acquired", new { receivedBytes = _receivedBytes, activated = false });
        }
    }

    private void FlushCredit()
    {
        if (!_creditDirty) return;
        uint[] consumed = _receivePolicy.ConsumedCounters;
        Send(new FileCreditMessage { SessionId = _manifest!.SessionId, CreditSequence = ++_creditSequence,
            ReleasedBytes = _releasedBytes, ReleasedFrames = (uint[])_releasedFrames.Clone(), ConsumedCounters = consumed });
        _receivePolicy.CreditSent(_releasedBytes, _releasedFrames, consumed);
        _creditDirty = false; _lastCreditAt = Program.Clock.Elapsed.TotalSeconds;
    }

    private void Send(IAnomalyMessage message)
    {
        using var stream = new MemoryStream(); using var binary = new BinaryWriter(stream);
        binary.Write((byte)0xFE); binary.Write(_replay.NextTxCounter(message.TransportChannel));
        binary.Write(AnomalyMessageRegistry.ComputeHash(message.MessageName)); message.Serialize(binary);
        byte[] envelope = AuthTransportCrypto.Encrypt(_keys!.ClientToServerKey, _keys.SessionId, _keys.TranscriptHash,
            AuthTransportDirection.ClientToServer, stream.ToArray());
        var writer = new NetworkWriter();
        writer.WriteUShort(_carrierId); writer.WriteByte(1); writer.WriteInt(envelope.Length); writer.WriteBytes(envelope, 0, envelope.Length);
        // Batcher/Unbatcher use the game's global, unlocked writer pool. Keep
        // worker framing privately owned rather than racing the server main thread.
        var batch = new NetworkWriter();
        batch.WriteDouble(Program.Clock.Elapsed.TotalSeconds);
        Compression.CompressVarUInt(batch, (ulong)writer.Position);
        batch.WriteBytes(writer.buffer, 0, writer.Position);
        var segment = batch.ToArraySegment();
        if (message is FileAckMessage)
        {
            _pendingAckDeliveries++;
            _peer!.SendWithDeliveryEvent(segment.Array!, segment.Offset, segment.Count, 0, DeliveryMethod.ReliableOrdered, new AckDelivery());
        }
        else _peer!.Send(segment.Array!, segment.Offset, segment.Count, 0, DeliveryMethod.ReliableOrdered);
    }

    private void Fail(string error)
    {
        if (_error != null) return;
        _error = error; Record("failure", new { error }); ReleaseOfferBarrier();
    }
    private void ReleaseOfferBarrier()
    {
        if (_offerBarrierReleased) return;
        _offerBarrierReleased = true; _offers.Signal();
    }
    private void Record(string stage, object details) => File.AppendAllText(Path.Combine(_output, "events.jsonl"),
        JsonSerializer.Serialize(new { utc = DateTime.UtcNow, elapsedSeconds = Program.Clock.Elapsed.TotalSeconds, stage, details }) + "\n");

    private sealed class AckDelivery { }

    private sealed class ReceivedFile : IDisposable
    {
        internal readonly FileManifestMessage.FileEntry Entry;
        internal readonly string Path;
        internal readonly bool[] Chunks;
        internal FileStream? Stream;
        internal int Written;
        internal bool CompleteSeen, Acked;
        internal ReceivedFile(FileManifestMessage.FileEntry entry, string output)
        {
            Entry = entry; Path = System.IO.Path.Combine(output, Program.Hex(entry.Hash).ToLowerInvariant() + ".bin");
            Chunks = new bool[(entry.Size + ChunkBytes - 1) / ChunkBytes];
        }
        internal void Open()
        {
            if (Stream != null) return;
            Stream = new FileStream(Path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            Stream.SetLength(Entry.Size);
        }
        public void Dispose() => Stream?.Dispose();
    }
}
