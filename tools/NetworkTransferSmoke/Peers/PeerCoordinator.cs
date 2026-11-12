#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using MEC;
using LiteNetLib;
using Mirror.LiteNetLib4Mirror;
using UnityEngine;

namespace Anomaly.NetworkTransferPeers;

// Runs only in the owned Unity server's opt-in smoke plugin. The game fork's
// ReadOnlySpan signatures belong to Unity mscorlib, so a stock CLR executable
// is deliberately not used as a substitute for the actual transport runtime.
internal static class PeerCoordinator
{
    private static string? _evidence;
    private static CancellationTokenSource? _stop;
    private static Task? _work;
    private static DateTime _nextPoll;
    private static readonly ConcurrentDictionary<ulong, ExpectedSession> Expected = new ConcurrentDictionary<ulong, ExpectedSession>();
    private static readonly ConcurrentDictionary<ulong, ServerCompletionProof> Completed = new ConcurrentDictionary<ulong, ServerCompletionProof>();
    private static readonly ConcurrentDictionary<ulong, ServerCleanupProof> Cancelled = new ConcurrentDictionary<ulong, ServerCleanupProof>();
    private static readonly ConcurrentDictionary<ulong, HoleExpectation> Holes = new ConcurrentDictionary<ulong, HoleExpectation>();
    private static readonly ConcurrentDictionary<ulong, ServerHoleProof> BlockedHoles = new ConcurrentDictionary<ulong, ServerHoleProof>();
    private static FieldInfo? _activeSessions, _sessionId, _requestedBytes, _requestedFiles, _pump;
    private static FieldInfo? _admissions, _authConnections, _authSync;
    private static PropertyInfo? _isComplete;
    private static string? _observationError;
    private static CoroutineHandle _observerLoop;
    private static double _lastSenderSample;
    private static long _lastFrame;

    internal static string? ObservationError => Volatile.Read(ref _observationError);

    internal static void ExpectSession(ulong sessionId, int files, long bytes, bool expectedCancellation = false)
    {
        if (sessionId == 0 || files <= 0 || bytes <= 0 || !Expected.TryAdd(sessionId, new ExpectedSession(files, bytes, expectedCancellation)))
            throw new InvalidOperationException("Invalid or repeated protocol-peer session expectation.");
    }

    internal static bool TryGetCompletion(ulong sessionId, out ServerCompletionProof proof) => Completed.TryGetValue(sessionId, out proof!);

    internal static bool HasObservedOwner(ulong sessionId) => Expected.TryGetValue(sessionId, out var expected) && Volatile.Read(ref expected.OwnerObserved) != 0;
    internal static void ExpectCancellation(ulong sessionId)
    {
        if (!Expected.TryGetValue(sessionId, out var expected) || !expected.Cancellation || Volatile.Read(ref expected.OwnerObserved) == 0 ||
            Interlocked.CompareExchange(ref expected.CancelRequested, 1, 0) != 0)
            throw new InvalidOperationException("Cancellation requires one independently observed expected owner.");
    }
    internal static bool TryGetCleanup(ulong sessionId, out ServerCleanupProof proof) => Cancelled.TryGetValue(sessionId, out proof!);
    internal static void ExpectHole(ulong sessionId, byte domain, uint counterBase)
    {
        if (!Expected.ContainsKey(sessionId) || domain >= 16 || counterBase > uint.MaxValue - 9 ||
            !Holes.TryAdd(sessionId, new HoleExpectation(domain, counterBase)))
            throw new InvalidOperationException("Invalid or repeated original-chunk hole expectation.");
    }
    internal static bool TryGetHole(ulong sessionId, out ServerHoleProof proof) => BlockedHoles.TryGetValue(sessionId, out proof!);

    internal static bool AllSessionsCompleted(int count, int cancellations = 0) => Expected.Count == count + cancellations &&
        Completed.Count == count && Cancelled.Count == cancellations && ObservationError == null;

    internal static void Initialize(string evidence)
    {
        if (Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_PEERS") != "1") return;
        _evidence = evidence;
        _stop = new CancellationTokenSource();
        Expected.Clear(); Completed.Clear(); Cancelled.Clear(); Holes.Clear(); BlockedHoles.Clear(); _observationError = null;
        _work = null; _nextPoll = default; _lastFrame = 0; _lastSenderSample = 0;
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var type = typeof(Anomaly.Server.Plugin).Assembly.GetType("Anomaly.Server.FileTransfer.FileTransferSession", true)!;
        _activeSessions = type.GetField("ActiveSessions", all) ?? throw new MissingFieldException(type.FullName, "ActiveSessions");
        _sessionId = type.GetField("_sessionId", all) ?? throw new MissingFieldException(type.FullName, "_sessionId");
        _requestedBytes = type.GetField("_requestedBytes", all) ?? throw new MissingFieldException(type.FullName, "_requestedBytes");
        _requestedFiles = type.GetField("_requestedFiles", all) ?? throw new MissingFieldException(type.FullName, "_requestedFiles");
        _pump = type.GetField("Pump", all) ?? throw new MissingFieldException(type.FullName, "Pump");
        _isComplete = type.GetProperty("IsComplete", all) ?? throw new MissingMemberException(type.FullName, "IsComplete");
        var bootstrap = type.Assembly.GetType("Anomaly.Server.Networking.ConnectionContentBootstrap", true)!;
        _admissions = bootstrap.GetField("Admissions", all) ?? throw new MissingFieldException(bootstrap.FullName, "Admissions");
        var auth = type.Assembly.GetType("Anomaly.Server.Networking.AuthenticatedTransportSessions", true)!;
        _authConnections = auth.GetField("Connections", all) ?? throw new MissingFieldException(auth.FullName, "Connections");
        _authSync = auth.GetField("Sync", all) ?? throw new MissingFieldException(auth.FullName, "Sync");
        _observerLoop = Timing.RunCoroutine(ObserveLoop());
    }

    internal static void Shutdown()
    {
        if (_evidence == null) return;
        Timing.KillCoroutines(_observerLoop);
        _stop!.Cancel();
        // Never block the game thread waiting for peer network/disk workers.
        _evidence = null;
    }

    private static void Tick()
    {
        if (_evidence == null) return;
        ObserveServerCompletion(); // Production dictionaries are read only on their owning game thread.
        if (_work != null || DateTime.UtcNow < _nextPoll) return;
        _nextPoll = DateTime.UtcNow.AddMilliseconds(250);
        string trigger = Path.Combine(_evidence, "start-protocol-peers");
        if (!File.Exists(trigger)) return;
        string output = Path.Combine(_evidence, "protocol-peers");
        string[] args = {
            Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_SERVER_IDENTITY") ?? string.Empty,
            Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECTED_CONTENT") ?? string.Empty,
            Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_CLIENT_ARTIFACT") ?? string.Empty,
            output, Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_PEER_COUNT") ?? "3"
        };
        var token = _stop!.Token;
        _work = Task.Run(() =>
        {
            int result = Program.Run(args, token);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "protocol-peers-exit-code.txt"), result.ToString());
        });
    }

    private static IEnumerator<float> ObserveLoop()
    {
        yield return Timing.WaitForOneFrame;
        while (_evidence != null)
        {
            Tick();
            yield return Timing.WaitForOneFrame;
        }
    }

    private static void ObserveServerCompletion()
    {
        if (Expected.IsEmpty || ObservationError != null) return;
        try
        {
            var sessions = _activeSessions!.GetValue(null) as IDictionary ?? throw new InvalidOperationException("Server session dictionary unavailable.");
            object pump = _pump!.GetValue(null)!;
            var pumpEntries = Read(pump, "_entries") as IDictionary ?? throw new InvalidOperationException("Server pump dictionary unavailable.");
            long frame = Stopwatch.GetTimestamp();
            double? frameGap = _lastFrame == 0 ? (double?)null : (frame - _lastFrame) * 1000d / Stopwatch.Frequency;
            _lastFrame = frame;
            bool sample = Program.Clock.Elapsed.TotalSeconds - _lastSenderSample >= .25;
            var rows = sample ? new List<object>() : null;
            foreach (DictionaryEntry entry in sessions)
            {
                object session = entry.Value;
                ulong id = (ulong)_sessionId!.GetValue(session)!;
                if (!Expected.TryGetValue(id, out var expected)) continue;
                CaptureOwner(expected, session, entry.Key);
                ObserveHole(id, session);
                if (sample)
                {
                    object? credits = Read(session, "_credits");
                    var sending = Read(session, "_sendingFiles") as IEnumerable ?? throw new InvalidOperationException("Missing sending file list.");
                    var filesInFlight = new List<object>();
                    foreach (object file in sending)
                    {
                        object? reader = Read(file, "Reader");
                        filesInFlight.Add(new { nextChunk = Read(file, "NextChunk"), chunkCount = Read(file, "ChunkCount"),
                            completionSent = Read(file, "CompletionSent"), verified = Read(file, "Verified"),
                            reader = reader == null ? null : new { completed = Read(reader, "IsCompleted"), error = Read(reader, "Error"),
                                prefetchedChunks = Read(Read(reader, "_chunks"), "Count") } });
                    }
                    rows!.Add(new { session = id, state = Read(session, "_state")?.ToString(),
                        expectedOutcome = expected.Cancellation ? "Cancelled" : "Complete", cancellationRequested = Volatile.Read(ref expected.CancelRequested) != 0,
                        pumpTracked = pumpEntries.Contains(session),
                        requestedBytes = Read(session, "_requestedBytes"), pendingFiles = Read(Read(session, "_pendingFiles"), "Count"),
                        filesInFlight, credits = credits == null ? null : new { outstandingBytes = Read(credits, "OutstandingBytes"),
                            outstandingFrames = Read(credits, "OutstandingFrames"), sentFrames = Read(credits, "_sent"),
                            releasedFrames = Read(credits, "_released"), lastCounters = Read(credits, "_lastCounters"),
                            consumedCounters = Read(credits, "_consumedCounters") } });
                }
                if (Completed.ContainsKey(id) || !(bool)_isComplete!.GetValue(session)!) continue;
                if (expected.Cancellation) throw new InvalidOperationException("An expected cancelled session unexpectedly completed acquisition.");
                var files = _requestedFiles!.GetValue(session) as IDictionary ?? throw new InvalidOperationException("Server file dictionary unavailable.");
                int verified = 0;
                foreach (DictionaryEntry file in files)
                {
                    object value = file.Value;
                    var flag = value.GetType().GetField("Verified", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? throw new MissingFieldException(value.GetType().FullName, "Verified");
                    if ((bool)flag.GetValue(value)!) verified++;
                }
                long requested = (long)_requestedBytes!.GetValue(session)!;
                if (files.Count != expected.Files || verified != expected.Files || requested != expected.Bytes)
                    throw new InvalidOperationException("Server Complete state does not match every requested verified file/byte.");
                Completed.TryAdd(id, new ServerCompletionProof(id, verified, requested, Program.Clock.Elapsed.TotalSeconds));
            }
            foreach (var entry in Expected)
                if (entry.Value.Cancellation && Volatile.Read(ref entry.Value.CancelRequested) != 0 && !Cancelled.ContainsKey(entry.Key))
                    ObserveCleanup(entry.Key, entry.Value, sessions, pumpEntries);
            if (sample)
            {
                _lastSenderSample = Program.Clock.Elapsed.TotalSeconds;
                File.AppendAllText(Path.Combine(_evidence!, "protocol-peer-server.jsonl"), JsonSerializer.Serialize(new {
                    utc = DateTime.UtcNow, elapsedSeconds = _lastSenderSample, frameGapMs = frameGap,
                    idleActive = IdleMode.IdleModeActive, pauseIdle = IdleMode.PauseIdleMode,
                    targetFrameRate = Application.targetFrameRate, timeScale = Time.timeScale,
                    hubCount = ReferenceHub.AllHubs.Count, connectedPeers = LiteNetLib4MirrorCore.Host?.ConnectedPeersCount,
                    pumpSources = pumpEntries.Count, pumpReady = Read(Read(pump, "_ready"), "Count"),
                    pumpGlobalTokens = Read(pump, "_globalTokens"),
                    admissionCapacity = Read(pump, "AdmissionCapacity") ?? throw new InvalidOperationException("Missing scheduler admission capacity."),
                    sessions = rows }) + "\n");
            }
        }
        catch (Exception error) { Volatile.Write(ref _observationError, error.ToString()); }
    }

    private static void CaptureOwner(ExpectedSession expected, object session, object connection)
    {
        if (!expected.Cancellation) return;
        if (expected.Session != null && !ReferenceEquals(expected.Session, session))
            throw new InvalidOperationException("A protocol-peer session identifier changed owners.");
        expected.Session = session; expected.Connection = connection;
        expected.Snapshot = Read(session, "_assetSnapshot") ?? throw new InvalidOperationException("Missing owned snapshot.");
        if (expected.Cancellation)
        {
            var sending = Read(session, "_sendingFiles") as IEnumerable ?? throw new InvalidOperationException("Missing sending file list.");
            foreach (object file in sending)
            {
                object? reader = Read(file, "Reader");
                if (reader != null) expected.Readers.Add(reader);
            }
            lock (_authSync!.GetValue(null)!)
            {
                var connections = _authConnections!.GetValue(null) as IDictionary ?? throw new InvalidOperationException("Missing authenticated connection dictionary.");
                if (connections.Contains(connection)) expected.AuthOwner = connections[connection];
            }
            if (expected.AuthOwner == null) throw new InvalidOperationException("The expected cancellation owner has no authenticated transport session.");
            int connectionId = Convert.ToInt32(Read(connection, "connectionId"));
            if (LiteNetLib4MirrorServer.Peers == null || !LiteNetLib4MirrorServer.Peers.TryGetValue(connectionId, out var transportPeer))
                throw new InvalidOperationException("The expected cancellation owner has no real transport peer mapping.");
            if (expected.TransportPeer != null && !ReferenceEquals(expected.TransportPeer, transportPeer))
                throw new InvalidOperationException("The expected cancellation transport peer changed owners.");
            expected.TransportPeer = transportPeer;
        }
        Volatile.Write(ref expected.OwnerObserved, 1);
    }

    private static void ObserveCleanup(ulong id, ExpectedSession expected, IDictionary sessions, IDictionary pumpEntries)
    {
        object session = expected.Session ?? throw new InvalidOperationException("Cancellation owner was never observed.");
        object connection = expected.Connection!;
        if (sessions.Contains(connection) || pumpEntries.Contains(session)) return;
        var ready = Read(_pump!.GetValue(null), "_ready") as IEnumerable ??
            throw new InvalidOperationException("Missing physical scheduler queue.");
        foreach (object entry in ready)
            if (ReferenceEquals(Read(entry, "Source"), session)) return;
        if (!(bool)Read(session, "_disposed")! || (bool)Read(session, "_budgetReserved")! || Read(session, "_state")?.ToString() != "Aborted")
            throw new InvalidOperationException("A cancelled session disappeared without releasing its active owner.");
        var admissions = _admissions!.GetValue(null) as IDictionary ?? throw new InvalidOperationException("Missing content admission dictionary.");
        if (admissions.Contains(connection)) return;
        int connectionId = Convert.ToInt32(Read(connection, "connectionId"));
        if (Mirror.NetworkServer.connections.TryGetValue(connectionId, out var mirrorOwner) && ReferenceEquals(mirrorOwner, connection)) return;
        if (LiteNetLib4MirrorServer.Peers != null && LiteNetLib4MirrorServer.Peers.TryGetValue(connectionId, out var nativeOwner) &&
            ReferenceEquals(nativeOwner, expected.TransportPeer)) return;
        if (expected.TransportPeer == null || expected.TransportPeer.ConnectionState == ConnectionState.Connected) return;
        lock (_authSync!.GetValue(null)!)
        {
            var auth = _authConnections!.GetValue(null) as IDictionary ?? throw new InvalidOperationException("Missing authenticated connection dictionary.");
            if (auth.Contains(connection)) return;
            foreach (string secret in new[] { "ClientToServerKey", "ServerToClientKey", "SessionId", "TranscriptHash" })
            {
                var bytes = Read(expected.AuthOwner, secret) as byte[] ?? throw new InvalidOperationException("Missing retired authenticated-session key buffer.");
                foreach (byte value in bytes)
                    if (value != 0) throw new InvalidOperationException("Retired authenticated transport material was not cleared.");
            }
        }
        foreach (object file in (IEnumerable)Read(session, "_sendingFiles")!)
            if (Read(file, "Reader") != null) throw new InvalidOperationException("Cancelled session retained an active spool reader.");
        foreach (object reader in expected.Readers)
            if (Convert.ToInt32(Read(reader, "_disposed")) == 0 || !(bool)Read(reader, "IsCompleted")!) return;
        long reservedBytes = 0; int reservedCount = 0, queuedCount = 0;
        foreach (DictionaryEntry entry in sessions)
        {
            object active = entry.Value;
            if ((bool)Read(active, "_budgetReserved")!) { reservedCount++; reservedBytes += (long)Read(active, "_requestedBytes")!; }
            if (!(bool)Read(active, "_disposed")! && Read(active, "_state")?.ToString() == "Queued") queuedCount++;
        }
        if ((long)ReadStatic(_activeSessions!.DeclaringType!, "_globalReservedBytes")! != reservedBytes ||
            (int)ReadStatic(_activeSessions.DeclaringType!, "_reservedTransferCount")! != reservedCount ||
            (int)ReadStatic(_activeSessions.DeclaringType!, "_queuedTransferCount")! != queuedCount)
            throw new InvalidOperationException("Cancelled owner left inconsistent global reservation or queue accounting.");
        var proof = new ServerCleanupProof(id, expected.Readers.Count, Convert.ToInt32(Read(expected.Snapshot, "_references")),
            reservedCount, queuedCount, reservedBytes, Program.Clock.Elapsed.TotalSeconds);
        if (Cancelled.TryAdd(id, proof))
        {
            RecordProof("cancelled-owner-released", proof.Report());
            // Do not let the observer itself retain a retired connection or its weak route entry.
            expected.Session = expected.Connection = expected.Snapshot = expected.AuthOwner = null;
            expected.TransportPeer = null; expected.Readers.Clear();
        }
    }

    private static void ObserveHole(ulong id, object session)
    {
        if (!Holes.TryGetValue(id, out var hole) || BlockedHoles.ContainsKey(id)) return;
        object credits = Read(session, "_credits") ?? throw new InvalidOperationException("Held chunk has no server credit ledger.");
        uint last = ((uint[])Read(credits, "_lastCounters")!)[hole.Domain];
        uint consumed = ((uint[])Read(credits, "_consumedCounters")!)[hole.Domain];
        uint released = ((uint[])Read(credits, "_released")!)[hole.Domain];
        var canSendMethod = credits.GetType().GetMethod("CanSend", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(credits.GetType().FullName, "CanSend");
        bool canSend = (bool)canSendMethod.Invoke(credits, new object[] { (int)hole.Domain, 1 })!;
        double now = Program.Clock.Elapsed.TotalSeconds;
        if (!PeerReceivePolicy.IsBlockedDispatchHole(hole.CounterBase, last, consumed, released, canSend))
        { hole.BlockedSince = 0; return; }
        if (hole.BlockedSince == 0) { hole.BlockedSince = now; return; }
        if (now - hole.BlockedSince < .25) return;
        var proof = new ServerHoleProof(id, hole.Domain, hole.CounterBase, last, consumed, released, now - hole.BlockedSince, now);
        if (BlockedHoles.TryAdd(id, proof)) RecordProof("dispatch-hole-span-blocked", proof.Report());
    }

    private static object? ReadStatic(Type type, string name) =>
        (type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, name)).GetValue(null);

    private static void RecordProof(string stage, object proof) => File.AppendAllText(Path.Combine(_evidence!, "protocol-peer-proofs.jsonl"),
        JsonSerializer.Serialize(new { utc = DateTime.UtcNow, stage, proof }) + "\n");

    private static object? Read(object? owner, string name)
    {
        if (owner == null) return null;
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = owner.GetType();
        var field = type.GetField(name, all);
        if (field != null) return field.GetValue(owner);
        var property = type.GetProperty(name, all) ?? throw new MissingMemberException(type.FullName, name);
        return property.GetValue(owner);
    }

    private sealed class ExpectedSession
    {
        internal readonly int Files;
        internal readonly long Bytes;
        internal readonly bool Cancellation;
        internal int OwnerObserved, CancelRequested;
        internal object? Session, Connection, Snapshot, AuthOwner;
        internal NetPeer? TransportPeer;
        internal readonly HashSet<object> Readers = new HashSet<object>();
        internal ExpectedSession(int files, long bytes, bool cancellation) { Files = files; Bytes = bytes; Cancellation = cancellation; }
    }

    private sealed class HoleExpectation
    {
        internal readonly byte Domain;
        internal readonly uint CounterBase;
        internal double BlockedSince;
        internal HoleExpectation(byte domain, uint counterBase) { Domain = domain; CounterBase = counterBase; }
    }

    internal sealed class ServerCleanupProof
    {
        private readonly ulong _session;
        private readonly int _readers, _snapshotReferences, _reserved, _queued;
        private readonly long _reservedBytes;
        private readonly double _observed;
        internal ServerCleanupProof(ulong session, int readers, int snapshotReferences, int reserved, int queued, long bytes, double observed)
        { _session = session; _readers = readers; _snapshotReferences = snapshotReferences; _reserved = reserved; _queued = queued; _reservedBytes = bytes; _observed = observed; }
        internal object Report() => new { session = _session, state = "CancelledOwnerReleased", activeSessionAbsent = true, pumpOwnerAbsent = true,
            pumpPhysicalQueueOwnerAbsent = true,
            contentAdmissionAbsent = true, authenticatedOwnerAbsent = true, retiredTransportMaterialCleared = true,
            mirrorConnectionOwnerAbsent = true, liteNetLibPeerMappingOwnerAbsent = true, retiredPeerConnected = false,
            readersDisposedAndCompleted = _readers, sharedSnapshotReferencesRemaining = _snapshotReferences,
            sharedSpoolDeletionClaimed = false, reservationAccountingConsistent = true,
            currentReservedTransfers = _reserved, currentQueuedTransfers = _queued, currentReservedBytes = _reservedBytes,
            observedSeconds = _observed, observedOnServerMainThread = true };
    }

    internal sealed class ServerHoleProof
    {
        private readonly ulong _session;
        private readonly byte _domain;
        private readonly uint _base, _last, _consumed, _released;
        private readonly double _blockedSeconds, _observed;
        internal ServerHoleProof(ulong session, byte domain, uint counterBase, uint last, uint consumed, uint released, double blockedSeconds, double observed)
        { _session = session; _domain = domain; _base = counterBase; _last = last; _consumed = consumed; _released = released; _blockedSeconds = blockedSeconds; _observed = observed; }
        internal object Report() => new { session = _session, domain = _domain, counterBase = _base, lastCounter = _last, consumedCounter = _consumed,
            releasedFrames = _released, canSendOneByte = false, observedBlockedSeconds = _blockedSeconds, observedSeconds = _observed,
            observedOnServerMainThread = true, scope = "Real wire credits with application dispatch reordering; not packet loss." };
    }

    internal sealed class ServerCompletionProof
    {
        private readonly ulong _session;
        private readonly int _files;
        private readonly long _bytes;
        private readonly double _observedAt;
        internal ServerCompletionProof(ulong session, int files, long bytes, double observedAt)
        { _session = session; _files = files; _bytes = bytes; _observedAt = observedAt; }
        internal object Report() => new { session = _session, state = "Complete", verifiedFiles = _files,
            requestedBytes = _bytes, observedSeconds = _observedAt, observedOnServerMainThread = true };
    }
}
