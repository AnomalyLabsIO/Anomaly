using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LiteNetLib;
using Mirror.LiteNetLib4Mirror;

namespace Anomaly.ServerApiSmoke;

// Opt-in test instrumentation. Never alters the production scheduler or credits.
internal static class NetworkTransferProbe
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly List<double> TickMilliseconds = new List<double>();
    private static readonly Dictionary<int, long> LaneBytes = new Dictionary<int, long>();
    private static readonly Dictionary<int, int> LaneBatches = new Dictionary<int, int>();
    private static readonly Dictionary<string, long> SkippedReliableQueueSamples = new Dictionary<string, long>();
    private static Harmony _harmony;
    private static string _evidence;
    private static Type _sessions;
    private static FieldInfo _peerChannels;
    private static object _configuredHost;
    private static long _lastFrame, _lastSample;
    private static double _maxFrameGap, _maxOutstandingBytes, _maxOutstandingFrames;
    private static double _maxActiveFrameGap;
    private static bool _hasTransferringSession;
    private static int _rtt, _loss;
    private static string _error;
    private static long _reliableQueueSamples;
    private static int _mainThreadId;
    private static object _lastAdmissionCapacity;
    private static long _admissionRejections;
    private static long _afkKickEvents, _playerKickingEvents;

    internal static void Initialize(string evidence)
    {
        if (Environment.GetEnvironmentVariable("ANOMALY_NETWORK_TRANSFER_SMOKE") != "1") return;
        TickMilliseconds.Clear(); LaneBytes.Clear(); LaneBatches.Clear();
        SkippedReliableQueueSamples.Clear(); _reliableQueueSamples = 0;
        _mainThreadId = Thread.CurrentThread.ManagedThreadId; _lastAdmissionCapacity = null; _admissionRejections = 0;
        _afkKickEvents = _playerKickingEvents = 0;
        _lastFrame = _lastSample = 0;
        _maxFrameGap = _maxActiveFrameGap = _maxOutstandingBytes = _maxOutstandingFrames = 0;
        _hasTransferringSession = false; _configuredHost = null;
        _error = null;
        _evidence = evidence;
        _rtt = Parse("ANOMALY_TRANSFER_RTT_MS", 0, 100);
        _loss = Parse("ANOMALY_TRANSFER_LOSS_PERCENT", 0, 20);
        _sessions = typeof(Anomaly.Server.Plugin).Assembly.GetType("Anomaly.Server.FileTransfer.FileTransferSession", true);
        _peerChannels = typeof(NetPeer).GetField("_channels", All)
            ?? throw new MissingFieldException(typeof(NetPeer).FullName, "_channels");
        if (!_peerChannels.FieldType.IsArray || _peerChannels.FieldType.GetArrayRank() != 1)
            throw new InvalidOperationException("The peer _channels instrumentation no longer identifies an array.");
        _harmony = new Harmony("anomaly.smoke.network-transfer");
        _harmony.Patch(_sessions.GetMethod("Tick", All),
            prefix: new HarmonyMethod(typeof(NetworkTransferProbe).GetMethod(nameof(BeforeTick), All)),
            postfix: new HarmonyMethod(typeof(NetworkTransferProbe).GetMethod(nameof(AfterTick), All)));
        _harmony.Patch(typeof(LiteNetLib4MirrorTransport).GetMethod("ServerSend", new[] { typeof(int), typeof(ArraySegment<byte>), typeof(int) }),
            prefix: new HarmonyMethod(typeof(NetworkTransferProbe).GetMethod(nameof(ObserveLane), All)));
        var bootstrap = typeof(Anomaly.Server.Plugin).Assembly.GetType("Anomaly.Server.Networking.ConnectionBootstrap", true);
        _harmony.Patch(bootstrap.GetMethod("Reject", All, null, new[] { typeof(Mirror.NetworkConnectionToClient), typeof(string) }, null)
            ?? throw new MissingMethodException(bootstrap.FullName, "Reject"),
            prefix: new HarmonyMethod(typeof(NetworkTransferProbe).GetMethod(nameof(ObserveAdmissionRejection), All)));
        StaticUnityMethods.OnUpdate += Observe;
        AFK.AFKManager.OnAFKKick += ObserveAfkKick;
        PlayerEvents.Kicking += ObservePlayerKicking;
    }

    internal static void Shutdown()
    {
        if (_evidence == null) return;
        StaticUnityMethods.OnUpdate -= Observe;
        AFK.AFKManager.OnAFKKick -= ObserveAfkKick;
        PlayerEvents.Kicking -= ObservePlayerKicking;
        File.WriteAllText(Path.Combine(_evidence, "transfer-server-summary.json"), Report());
        _harmony?.UnpatchAll("anomaly.smoke.network-transfer");
        _harmony = null;
        _evidence = null;
    }

    private static void ObserveLane(int channelId, ArraySegment<byte> data)
    {
        if (_evidence == null || channelId < 6) return;
        LaneBytes.TryGetValue(channelId, out long bytes); LaneBytes[channelId] = bytes + data.Count;
        LaneBatches.TryGetValue(channelId, out int batches); LaneBatches[channelId] = batches + 1;
    }

    private static void BeforeTick(out long __state) => __state = Stopwatch.GetTimestamp();

    private static void ObserveAfkKick(ReferenceHub hub)
    {
        if (_evidence == null) return;
        try
        {
            RequireMainThread();
            RecordKickObservation("stock-afk-trigger", hub, Read(null, typeof(AFK.AFKManager), "_kickMessage") as string, null);
            _afkKickEvents++;
        }
        catch (Exception error) { Invalidate(error); }
    }

    private static void ObservePlayerKicking(PlayerKickingEventArgs args)
    {
        if (_evidence == null) return;
        try
        {
            RequireMainThread();
            RecordKickObservation("labapi-kicking-observation", args.Player?.ReferenceHub, args.Reason, args.IsAllowed);
            _playerKickingEvents++;
        }
        catch (Exception error) { Invalidate(error); }
    }

    private static void RecordKickObservation(string kind, ReferenceHub hub, string reason, bool? allowed)
    {
        File.AppendAllText(Path.Combine(_evidence, "player-kick-observations.jsonl"), JsonSerializer.Serialize(new {
            utc = DateTime.UtcNow, monotonicTimestamp = Stopwatch.GetTimestamp(), stopwatchFrequency = Stopwatch.Frequency,
            kind, playerId = hub == null ? (int?)null : hub.PlayerId, networkId = hub == null ? (uint?)null : hub.netId,
            connectionId = hub?.connectionToClient?.connectionId, role = hub?.roleManager?.CurrentRole?.RoleTypeId.ToString(),
            reason, allowedAtObservation = allowed, configuredAfkSeconds = Read(null, typeof(AFK.AFKManager), "_kickTime"),
            observedOnServerMainThread = true,
            note = "Read-only trigger/kicking observation; no event argument, kick timer or behavior is changed. Other handlers may subsequently alter a cancellable kicking event." }) + "\n");
    }

    // A void prefix records evidence without skipping or modifying rejection.
    private static void ObserveAdmissionRejection(Mirror.NetworkConnectionToClient __0, string __1)
    {
        if (_evidence == null) return;
        try
        {
            RequireMainThread();
            object pump = Read(null, _sessions, "Pump") ?? throw new InvalidOperationException("Missing bulk pump for rejection evidence.");
            object capacity = Read(pump, "AdmissionCapacity") ?? throw new InvalidOperationException("Missing admission capacity estimate.");
            _lastAdmissionCapacity = capacity;
            var sessions = Read(null, _sessions, "ActiveSessions") as IDictionary ?? throw new InvalidOperationException("Missing active session dictionary.");
            object session = __0 != null && sessions.Contains(__0) ? sessions[__0] : null;
            File.AppendAllText(Path.Combine(_evidence, "admission-rejections.jsonl"), JsonSerializer.Serialize(new {
                utc = DateTime.UtcNow, monotonicTimestamp = Stopwatch.GetTimestamp(), stopwatchFrequency = Stopwatch.Frequency,
                connectionId = __0 == null ? (int?)null : __0.connectionId, reason = __1,
                sessionId = Read(session, "_sessionId"), state = Read(session, "_state")?.ToString(),
                requestedBytes = Read(session, "_requestedBytes"), admissionCapacity = capacity,
                reservedBytes = Read(null, _sessions, "_globalReservedBytes"), queuedTransfers = Read(null, _sessions, "_queuedTransferCount"),
                pumpSources = Read(Read(pump, "_entries"), "Count"), observedOnServerMainThread = true,
                pumpQueuedEntries = Read(Read(pump, "_ready"), "Count"),
                activeCapacityWork = CapacityWork(sessions),
                note = "Neutral pre-rejection observation. The production rejection and cleanup continue unchanged." }) + "\n");
            _admissionRejections++;
        }
        catch (Exception error) { Invalidate(error); }
    }
    private static List<object> CapacityWork(IDictionary sessions)
    {
        var result = new List<object>();
        foreach (DictionaryEntry entry in sessions)
        {
            if (Read(entry.Value, "_state")?.ToString() != "Sending") continue;
            object[] arguments = { null };
            if (!(bool)_sessions.GetMethod("TryCapacityWork", All).Invoke(entry.Value, arguments))
                throw new InvalidOperationException("Active capacity work was unavailable during observation.");
            object work = arguments[0];
            result.Add(new { sessionId = Read(entry.Value, "_sessionId"), remainingBytes = Read(work, "RemainingBytes"),
                remainingAccountedBytes = Read(work, "RemainingAccountedBytes"), remainingSeconds = Read(work, "RemainingSeconds"),
                maximumAccountedBytesPerSecond = Read(work, "MaximumAccountedBytesPerSecond"),
                transactionFloorSeconds = Read(work, "TransactionFloorSeconds") });
        }
        return result;
    }
    private static void AfterTick(long __state)
    {
        if (_evidence == null || !_hasTransferringSession) return;
        if (TickMilliseconds.Count < 200000)
            TickMilliseconds.Add((Stopwatch.GetTimestamp() - __state) * 1000d / Stopwatch.Frequency);
    }

    private static void Observe()
    {
        if (_evidence == null || _error != null) return;
        try
        {
            RequireMainThread();
            long now = Stopwatch.GetTimestamp();
            if (_lastFrame != 0)
            {
                double gap = (now - _lastFrame) * 1000d / Stopwatch.Frequency;
                _maxFrameGap = Math.Max(_maxFrameGap, gap);
                if (_hasTransferringSession) _maxActiveFrameGap = Math.Max(_maxActiveFrameGap, gap);
            }
            _lastFrame = now;
            var host = LiteNetLib4MirrorCore.Host;
            if (host != null && !ReferenceEquals(host, _configuredHost))
            {
                host.SimulateLatency = _rtt != 0;
                host.SimulationMinLatency = host.SimulationMaxLatency = _rtt / 2;
                host.SimulatePacketLoss = _loss != 0;
                host.SimulationPacketLossChance = _loss;
                host.EnableStatistics = true;
                _configuredHost = host;
            }
            var sessions = Read(null, _sessions, "ActiveSessions") as IDictionary;
            var observations = new List<object>();
            _hasTransferringSession = false;
            if (sessions != null) foreach (DictionaryEntry entry in sessions)
            {
                object session = entry.Value, credits = Read(session, "_credits");
                if (Read(session, "_state")?.ToString() == "Sending") _hasTransferringSession = true;
                double bytes = Number(credits, "OutstandingBytes"), frames = Number(credits, "OutstandingFrames");
                _maxOutstandingBytes = Math.Max(_maxOutstandingBytes, bytes);
                _maxOutstandingFrames = Math.Max(_maxOutstandingFrames, frames);
                observations.Add(new { session = Read(session, "_sessionId"), connectionId = ((Mirror.NetworkConnectionToClient)entry.Key).connectionId,
                    state = Read(session, "_state")?.ToString(),
                    laneMask = Read(session, "_laneMask"), domains = Read(session, "_domainCount"), requestedBytes = Read(session, "_requestedBytes"),
                    outstandingBytes = bytes, outstandingFrames = frames, sentBytes = Number(credits, "SentBytes"), releasedBytes = Number(credits, "ReleasedBytes"),
                    domainSentFrames = Read(credits, "_sent"), domainReleasedFrames = Read(credits, "_released"),
                    domainCounterBases = Read(credits, "_counterBases"), domainLastCounters = Read(credits, "_lastCounters"),
                    domainConsumedCounters = Read(credits, "_consumedCounters") });
            }
            if ((now - _lastSample) / (double)Stopwatch.Frequency < .25) return;
            _lastSample = now;
            object pump = Read(null, _sessions, "Pump") ?? throw new InvalidOperationException("Missing bulk pump.");
            _lastAdmissionCapacity = Read(pump, "AdmissionCapacity") ?? throw new InvalidOperationException("Missing admission capacity estimate.");
            var peers = new List<object>();
            if (host != null) foreach (var peer in host)
            {
                var queue = ReliableQueueSample.Capture(host.ChannelsCount,
                    () => peer.ConnectionState == ConnectionState.Connected,
                    () => (Array)_peerChannels.GetValue(peer), peer.GetPacketsCountInReliableQueue);
                if (queue.Packets.HasValue) _reliableQueueSamples++;
                else
                {
                    SkippedReliableQueueSamples.TryGetValue(queue.Status, out long count);
                    SkippedReliableQueueSamples[queue.Status] = count + 1;
                }
                peers.Add(new { id = peer.Id, mtu = peer.Mtu, ping = peer.Ping, roundTripTimeMs = peer.RoundTripTime,
                    connectionState = peer.ConnectionState.ToString(), queuedPackets = queue.Packets,
                    reliableQueueObservation = queue.Status,
                    bytesSent = Number(peer.Statistics, "BytesSent"), bytesReceived = Number(peer.Statistics, "BytesReceived"),
                    packetsSent = Number(peer.Statistics, "PacketsSent"), packetsReceived = Number(peer.Statistics, "PacketsReceived"),
                    packetLoss = Number(peer.Statistics, "PacketLoss") });
            }
            var mirrorConnections = new List<object>();
            foreach (var entry in Mirror.NetworkServer.connections)
                mirrorConnections.Add(new { connectionId = entry.Key, typeName = entry.Value?.GetType().FullName,
                    isDummy = entry.Value is NetworkManagerUtils.Dummies.DummyNetworkConnection });
            var transportMappingIds = LiteNetLib4MirrorServer.Peers == null ? null : new List<int>(LiteNetLib4MirrorServer.Peers.Keys);
            var sample = new { utc = DateTime.UtcNow, rttSimulationMs = _rtt, incomingLossPercent = _loss,
                channels = host?.ChannelsCount, updateTimeMs = host?.UpdateTime, sessions = observations, peers,
                pumpSources = Read(Read(pump, "_entries"), "Count"),
                pumpQueuedEntries = Read(Read(pump, "_ready"), "Count"),
                contentAdmissionOwners = Read(Read(null, typeof(Anomaly.Server.Plugin).Assembly.GetType(
                    "Anomaly.Server.Networking.ConnectionContentBootstrap", true), "Admissions"), "Count"),
                authenticatedConnectionOwners = AuthenticatedConnectionOwnerCount(),
                mirrorConnectionOwners = Mirror.NetworkServer.connections.Count,
                mirrorConnections,
                transportPeerMappings = LiteNetLib4MirrorServer.Peers?.Count,
                transportMappingConnectionIds = transportMappingIds,
                idleActive = IdleMode.IdleModeActive, pauseIdle = IdleMode.PauseIdleMode,
                configuredAfkSeconds = Read(null, typeof(AFK.AFKManager), "_kickTime"),
                targetFrameRate = UnityEngine.Application.targetFrameRate, timeScale = UnityEngine.Time.timeScale,
                hubCount = ReferenceHub.AllHubs.Count,
                admissionCapacity = _lastAdmissionCapacity,
                acquisitionPending = Read(null, typeof(Anomaly.Server.Plugin).Assembly.GetType(
                    "Anomaly.Server.Networking.ConnectionContentBootstrap", true), "HasPendingAcquisition"),
                reservedBytes = Read(null, _sessions, "_globalReservedBytes"), queuedTransfers = Read(null, _sessions, "_queuedTransferCount") };
            File.AppendAllText(Path.Combine(_evidence, "transfer-server.jsonl"), JsonSerializer.Serialize(sample) + "\n");
            File.WriteAllText(Path.Combine(_evidence, "transfer-server-summary.json"), Report());
        }
        catch (Exception error) { Invalidate(error); }
    }

    internal static string Report()
    {
        double[] sorted = TickMilliseconds.ToArray(); Array.Sort(sorted);
        return JsonSerializer.Serialize(new { valid = _error == null, error = _error, rttSimulationMs = _rtt, incomingLossPercent = _loss,
            tickSamples = sorted.Length, tickP95Ms = Percentile(sorted, .95), tickP99Ms = Percentile(sorted, .99),
            tickMaxMs = Percentile(sorted, 1), maxFrameGapMs = _maxFrameGap, maxActiveFrameGapMs = _maxActiveFrameGap,
            maxOutstandingBytes = _maxOutstandingBytes, maxOutstandingFrames = _maxOutstandingFrames,
            reliableQueueSamples = _reliableQueueSamples, skippedReliableQueueSamples = SkippedReliableQueueSamples,
            admissionCapacity = _lastAdmissionCapacity, admissionRejections = _admissionRejections,
            afkKickEvents = _afkKickEvents, playerKickingEvents = _playerKickingEvents,
            laneBytes = LaneBytes, laneBatches = LaneBatches,
            note = "Observed bounds; 250ms reliable ordered/unordered outgoing queue samples cover Connected peers across all configured channels and omit inflight packets. Unsampled transient peers have null queuedPackets, never zero. Tick samples require an active Sending session in the previous frame. Per-frame session credits are observed. Whole Tick includes session maintenance, not just the 2ms pump." });
    }

    private static void RequireMainThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            throw new InvalidOperationException("Private network probe cannot inspect main-thread owners from another thread.");
    }

    private static int AuthenticatedConnectionOwnerCount()
    {
        RequireMainThread();
        var type = typeof(Anomaly.Server.Plugin).Assembly.GetType("Anomaly.Server.Networking.AuthenticatedTransportSessions", true);
        var snapshot = type.GetMethod("SnapshotConnections", All) ?? throw new MissingMethodException(type.FullName, "SnapshotConnections");
        return (snapshot.Invoke(null, null) as Array ?? throw new InvalidOperationException("Missing authenticated owner snapshot.")).Length;
    }

    private static void Invalidate(Exception error)
    {
        _error = error.ToString();
        // Probe failures must not change the original production reject behavior.
        try { File.AppendAllText(Path.Combine(_evidence, "transfer-probe-errors.log"), error + "\n"); }
        catch (Exception reporting) { _error += "\nProbe error-log write failed: " + reporting; }
        try
        {
            string report = Thread.CurrentThread.ManagedThreadId == _mainThreadId ? Report() :
                JsonSerializer.Serialize(new { valid = false, error = _error,
                    note = "Unexpected off-thread instrumentation call; main-thread metrics were not inspected." });
            File.WriteAllText(Path.Combine(_evidence, "transfer-server-summary.json"), report);
        }
        catch (Exception reporting) { _error += "\nProbe summary write failed: " + reporting; }
    }

    private static double Percentile(double[] values, double percentile) => values.Length == 0 ? 0 : values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * percentile) - 1)];
    private static int Parse(string name, int minimum, int maximum)
    {
        string text = Environment.GetEnvironmentVariable(name);
        int value = string.IsNullOrEmpty(text) ? 0 : int.Parse(text);
        if (value < minimum || value > maximum || name.EndsWith("RTT_MS") && value % 2 != 0) throw new ArgumentException("Invalid " + name);
        return value;
    }
    private static object Read(object owner, string name) => owner == null ? null : Read(owner, owner.GetType(), name);
    private static object Read(object owner, Type type, string name)
    {
        var field = type.GetField(name, All);
        if (field != null) return field.GetValue(owner);
        var property = type.GetProperty(name, All);
        if (property != null) return property.GetValue(owner);
        throw new MissingMemberException(type.FullName, name);
    }
    private static double Number(object owner, string name) => Convert.ToDouble(Read(owner, name) ?? 0);
}
