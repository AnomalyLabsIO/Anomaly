using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace NetworkImpairmentRelay;

/// <summary>Private qualification tool: opaque UDP payloads, independent flow sockets, bounded delayed datagrams.</summary>
internal sealed class UdpImpairmentRelay : IAsyncDisposable
{
    private readonly RelayOptions _options;
    private readonly object _gate = new();
    private readonly Socket _front = NewSocket();
    private readonly Dictionary<IPEndPoint, Flow> _flows = new();
    private readonly List<Task> _receivers = new();
    private readonly PriorityQueue<DelayedDatagram, (long Due, long Order)> _pending = new();
    private readonly SemaphoreSlim _changed = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DirectionStats _clientToServer = new();
    private readonly DirectionStats _serverToClient = new();
    private readonly List<string> _failures = new();
    private readonly long _delayTicks;
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private DateTimeOffset? _stopped;
    private long _order;
    private long _queueBytes;
    private long _queuePeakBytes;
    private int _queuePeakDatagrams;
    private int _flowSequence;
    private int _flowPeak;
    private int _expiredFlows;
    private double _maximumLateMilliseconds;
    private bool _running;

    internal UdpImpairmentRelay(RelayOptions options)
    {
        options.Validate();
        _options = options;
        _delayTicks = checked((long)Math.Ceiling(options.RttMilliseconds / 2000 * Stopwatch.Frequency));
    }

    internal async Task RunAsync(CancellationToken cancellation)
    {
        if (_running) throw new InvalidOperationException("A relay instance can only run once.");
        _running = true;
        using var registration = cancellation.Register(_shutdown.Cancel);
        try
        {
            _front.Bind(_options.Front);
            await WriteStatsAsync();
            await Task.WhenAll(ReceiveFrontAsync(), SendDueAsync(), MaintainAsync());
        }
        catch (Exception exception) when (!_shutdown.IsCancellationRequested)
        {
            Fail("Relay execution failed: " + exception);
        }
        finally
        {
            _shutdown.Cancel();
            _front.Dispose();
            Flow[] flows;
            Task[] receivers;
            lock (_gate)
            {
                flows = _flows.Values.ToArray();
                _flows.Clear();
                receivers = _receivers.ToArray();
                while (_pending.TryDequeue(out var datagram, out _))
                    Stats(datagram.ServerBound).ShutdownDiscardedDatagrams++;
                _queueBytes = 0;
            }
            foreach (var flow in flows) flow.Close();
            await Task.WhenAll(receivers);
            lock (_gate) _stopped = DateTimeOffset.UtcNow;
            await WriteStatsAsync();
        }
    }

    private async Task ReceiveFrontAsync()
    {
        byte[] buffer = new byte[65535];
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                SocketReceiveFromResult received;
                try
                {
                    received = await _front.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), _shutdown.Token);
                }
                catch (SocketException exception) when (IsTransientIcmp(exception))
                {
                    lock (_gate) _clientToServer.TransientIcmpNotifications++;
                    continue;
                }
                var client = (IPEndPoint)received.RemoteEndPoint;
                if (!IPAddress.IsLoopback(client.Address))
                {
                    Fail("Non-loopback client endpoint was rejected.");
                    return;
                }
                Flow? flow;
                lock (_gate)
                {
                    if (!_flows.TryGetValue(client, out flow))
                    {
                        if (_flows.Count >= _options.MaximumFlows)
                        {
                            Fail("Flow limit exceeded; qualification is invalid.");
                            return;
                        }
                        flow = new Flow(++_flowSequence, client, _options, _shutdown.Token);
                        _flows.Add(client, flow);
                        _flowPeak = Math.Max(_flowPeak, _flows.Count);
                        _receivers.Add(ReceiveUpstreamAsync(flow));
                    }
                }
                Enqueue(flow, serverBound: true, buffer.AsSpan(0, received.ReceivedBytes));
            }
        }
        catch (Exception exception) when (IsStopping(exception, _shutdown.Token)) { }
        catch (Exception exception) { Fail("Front receive failed: " + exception); }
    }

    private async Task ReceiveUpstreamAsync(Flow flow)
    {
        byte[] buffer = new byte[65535];
        try
        {
            while (!flow.Token.IsCancellationRequested)
            {
                int length;
                try
                {
                    length = await flow.Upstream.ReceiveAsync(buffer, SocketFlags.None, flow.Token);
                }
                catch (SocketException exception) when (IsTransientIcmp(exception))
                {
                    lock (_gate) _serverToClient.TransientIcmpNotifications++;
                    continue;
                }
                Enqueue(flow, serverBound: false, buffer.AsSpan(0, length));
            }
        }
        catch (Exception exception) when (IsStopping(exception, flow.Token)) { }
        catch (Exception exception) { Fail("Upstream receive failed: " + exception); }
        finally
        {
            flow.Close();
            flow.Cancellation.Dispose();
        }
    }

    private void Enqueue(Flow flow, bool serverBound, ReadOnlySpan<byte> payload)
    {
        lock (_gate)
        {
            if (_shutdown.IsCancellationRequested || !flow.Active) return;
            long now = Stopwatch.GetTimestamp();
            flow.LastSeen = now;
            var stats = Stats(serverBound);
            stats.ReceivedDatagrams++;
            stats.ReceivedBytes += payload.Length;
            var random = serverBound ? flow.UpstreamRandom : flow.DownstreamRandom;
            if (random.NextDouble() * 100 < _options.LossPercent)
            {
                stats.SimulatedLostDatagrams++;
                stats.SimulatedLostBytes += payload.Length;
                return;
            }
            if (payload.Length > _options.MaximumQueueBytes - _queueBytes ||
                _pending.Count >= RelayOptions.MaximumQueuedDatagrams)
            {
                Fail("Delayed datagram queue limit exceeded; qualification is invalid.");
                return;
            }
            _pending.Enqueue(new DelayedDatagram(flow, serverBound, payload.ToArray()), (now + _delayTicks, ++_order));
            _queueBytes += payload.Length;
            _queuePeakBytes = Math.Max(_queuePeakBytes, _queueBytes);
            _queuePeakDatagrams = Math.Max(_queuePeakDatagrams, _pending.Count);
            if (_changed.CurrentCount == 0) _changed.Release();
        }
    }

    private async Task SendDueAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                DelayedDatagram? datagram = null;
                int waitMilliseconds = Timeout.Infinite;
                lock (_gate)
                {
                    if (_pending.TryPeek(out var next, out var priority))
                    {
                        long now = Stopwatch.GetTimestamp();
                        if (priority.Due <= now)
                        {
                            datagram = _pending.Dequeue();
                            _queueBytes -= datagram.Payload.Length;
                            _maximumLateMilliseconds = Math.Max(_maximumLateMilliseconds, (now - priority.Due) * 1000d / Stopwatch.Frequency);
                        }
                        else
                            waitMilliseconds = Math.Max(1, (int)Math.Ceiling((priority.Due - now) * 1000d / Stopwatch.Frequency));
                    }
                }
                if (datagram == null)
                {
                    await _changed.WaitAsync(waitMilliseconds, _shutdown.Token);
                    continue;
                }
                if (!datagram.Flow.Active)
                {
                    lock (_gate) Stats(datagram.ServerBound).ExpiredFlowDiscardedDatagrams++;
                    continue;
                }
                int sent = datagram.ServerBound
                    ? await datagram.Flow.Upstream.SendAsync(datagram.Payload, SocketFlags.None, _shutdown.Token)
                    : await _front.SendToAsync(datagram.Payload, SocketFlags.None, datagram.Flow.Client, _shutdown.Token);
                if (sent != datagram.Payload.Length)
                    throw new IOException("A UDP send did not preserve the datagram length.");
                lock (_gate)
                {
                    var stats = Stats(datagram.ServerBound);
                    stats.ForwardedDatagrams++;
                    stats.ForwardedBytes += sent;
                }
            }
        }
        catch (Exception exception) when (IsStopping(exception, _shutdown.Token)) { }
        catch (Exception exception) { Fail("Scheduled send failed: " + exception); }
    }

    private async Task MaintainAsync()
    {
        int step = 0;
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                await Task.Delay(100, _shutdown.Token);
                if (File.Exists(_options.StopFile))
                {
                    _shutdown.Cancel();
                    return;
                }
                if (++step % 10 != 0) continue;
                Flow[] expired;
                lock (_gate)
                {
                    _receivers.RemoveAll(task => task.IsCompleted);
                    long cutoff = Stopwatch.GetTimestamp() - (long)_options.IdleSeconds * Stopwatch.Frequency;
                    expired = _flows.Values.Where(flow => flow.LastSeen < cutoff).ToArray();
                    foreach (var flow in expired)
                    {
                        _flows.Remove(flow.Client);
                        _expiredFlows++;
                    }
                }
                foreach (var flow in expired) flow.Close();
                await WriteStatsAsync();
            }
        }
        catch (Exception exception) when (IsStopping(exception, _shutdown.Token)) { }
        catch (Exception exception) { Fail("Relay maintenance failed: " + exception); }
    }

    internal RelaySnapshot Snapshot()
    {
        lock (_gate)
            return new RelaySnapshot(_failures.Count == 0, _failures.ToArray(), _started, _stopped,
                _options.Front.ToString(), _options.Upstream.ToString(), _options.RttMilliseconds,
                _options.LossPercent, _options.Seed, _options.MaximumQueueBytes, RelayOptions.MaximumQueuedDatagrams,
                _options.MaximumFlows, _flows.Count, _flowSequence, _flowPeak, _expiredFlows, _queueBytes,
                _pending.Count, _queuePeakBytes, _queuePeakDatagrams, _maximumLateMilliseconds,
                OperatingSystem.IsWindows(), _clientToServer.Copy(), _serverToClient.Copy());
    }

    private Task WriteStatsAsync() => Program.WriteJsonAsync(_options.StatsPath, Snapshot());
    private DirectionStats Stats(bool serverBound) => serverBound ? _clientToServer : _serverToClient;

    private void Fail(string message)
    {
        lock (_gate) _failures.Add(message);
        _shutdown.Cancel();
    }

    private static bool IsStopping(Exception exception, CancellationToken cancellation) =>
        cancellation.IsCancellationRequested && exception is OperationCanceledException or ObjectDisposedException or SocketException;

    private static bool IsTransientIcmp(SocketException exception) =>
        exception.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused;

    private static Socket NewSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ExclusiveAddressUse = true,
            ReceiveBufferSize = 4 * 1024 * 1024,
            SendBufferSize = 4 * 1024 * 1024,
        };
        // A closed/retired UDP client legitimately sends ICMP port-unreachable.
        // On Windows the default reports that as WSAECONNRESET on an unrelated
        // later ReceiveFrom, which would otherwise terminate every relay flow.
        // Disable those notifications; the protocol owns connection liveness.
        if (OperatingSystem.IsWindows())
            socket.IOControl(unchecked((int)0x9800000C), new byte[4], null); // SIO_UDP_CONNRESET = FALSE
        return socket;
    }

    public ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _front.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class Flow
    {
        internal readonly IPEndPoint Client;
        internal readonly Socket Upstream;
        internal readonly CancellationTokenSource Cancellation;
        internal readonly CancellationToken Token;
        internal readonly Random UpstreamRandom;
        internal readonly Random DownstreamRandom;
        internal long LastSeen = Stopwatch.GetTimestamp();
        internal volatile bool Active = true;
        private int _closed;

        internal Flow(int sequence, IPEndPoint client, RelayOptions options, CancellationToken cancellation)
        {
            Client = client;
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            Token = Cancellation.Token;
            Upstream = NewSocket();
            Upstream.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            Upstream.Connect(options.Upstream);
            UpstreamRandom = new Random(unchecked(options.Seed * 486187739 + sequence * 16777619));
            DownstreamRandom = new Random(unchecked(options.Seed * 16777619 + sequence * 486187739 + 31));
        }

        internal void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            Active = false;
            Cancellation.Cancel();
            Upstream.Dispose();
        }
    }

    private sealed record DelayedDatagram(Flow Flow, bool ServerBound, byte[] Payload);
}

internal sealed class DirectionStats
{
    public long ReceivedDatagrams { get; set; }
    public long ReceivedBytes { get; set; }
    public long SimulatedLostDatagrams { get; set; }
    public long SimulatedLostBytes { get; set; }
    public long ForwardedDatagrams { get; set; }
    public long ForwardedBytes { get; set; }
    public long ShutdownDiscardedDatagrams { get; set; }
    public long ExpiredFlowDiscardedDatagrams { get; set; }
    public long TransientIcmpNotifications { get; set; }
    internal DirectionStats Copy() => (DirectionStats)MemberwiseClone();
}

internal sealed record RelaySnapshot(bool QualificationValid, string[] Failures, DateTimeOffset Started,
    DateTimeOffset? Stopped, string Front, string Upstream, double AddedRttMilliseconds, double LossPercentPerDirection,
    int Seed, long MaximumQueueBytes, int MaximumQueuedDatagrams, int MaximumFlows, int ActiveFlows, int TotalFlows,
    int PeakFlows, int ExpiredFlows, long QueuedBytes, int QueuedDatagrams, long PeakQueuedBytes, int PeakQueuedDatagrams,
    double MaximumSchedulingLatenessMilliseconds, bool WindowsUdpConnectionResetNotificationsSuppressed,
    DirectionStats ClientToServer, DirectionStats ServerToClient);
