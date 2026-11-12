using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace NetworkImpairmentRelay;

internal static class RelaySelfTest
{
    internal static async Task<int> RunAsync(string outputDirectory)
    {
        string root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var observations = new List<object>();
        try
        {
            foreach (int rtt in new[] { 0, 20, 50, 100 })
                observations.Add(await VerifyLatencyAndPayloadAsync(root, rtt));
            observations.Add(await VerifyLossAsync(root));
            observations.Add(await VerifyRetiredClientsAsync(root));
            observations.Add(await VerifyBoundAsync(root, flowLimit: false));
            observations.Add(await VerifyBoundAsync(root, flowLimit: true));
            await Program.WriteJsonAsync(Path.Combine(root, "self-test.json"), new { passed = true, observations });
            Console.WriteLine("Relay self-test passed: opaque UDP bytes, isolated flows, measured 0/20/50/100 ms, independent bidirectional loss, queue/flow fail-closed bounds.");
            return 0;
        }
        catch (Exception exception)
        {
            await Program.WriteJsonAsync(Path.Combine(root, "self-test.json"), new { passed = false, failure = exception.ToString(), observations });
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<object> VerifyLatencyAndPayloadAsync(string root, int rtt)
    {
        await using var echo = new EchoServer();
        string stem = Path.Combine(root, "latency-" + rtt);
        var options = Options(echo.Port, stem, rtt, 0);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var relay = new UdpImpairmentRelay(options);
        Task running = relay.RunAsync(stop.Token);
        var clients = Enumerable.Range(0, 3).Select(_ => Client(options.Front)).ToArray();
        var elapsed = new List<double>();
        long verifiedBytes = 0;
        try
        {
            int[] sizes = { 0, 1, 31, 1024, 16384, 60000 };
            for (int index = 0; index < 24; index++)
            {
                var client = clients[index % clients.Length];
                byte[] payload = Payload(index + rtt * 100, sizes[index % sizes.Length]);
                long started = Stopwatch.GetTimestamp();
                await client.SendAsync(payload, SocketFlags.None, stop.Token);
                byte[] response = new byte[65535];
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                int length = await client.ReceiveAsync(response, SocketFlags.None, timeout.Token);
                double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Check(length == payload.Length && SHA256.HashData(payload).AsSpan().SequenceEqual(SHA256.HashData(response.AsSpan(0, length))),
                    "Echo payload/hash changed at RTT " + rtt);
                Check(milliseconds >= rtt - 2, "Datagram escaped the configured delay: " + milliseconds);
                elapsed.Add(milliseconds);
                verifiedBytes += length;
            }
            await File.WriteAllTextAsync(options.StopFile, "self-test complete");
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            var snapshot = relay.Snapshot();
            Check(snapshot.QualificationValid && snapshot.TotalFlows == 3 && snapshot.ClientToServer.ForwardedDatagrams == 24 &&
                snapshot.ServerToClient.ForwardedDatagrams == 24 && snapshot.PeakQueuedBytes <= options.MaximumQueueBytes,
                "Latency/flow accounting failed.");
            double[] ordered = elapsed.Order().ToArray();
            return new
            {
                test = "payload-and-rtt", requestedRttMilliseconds = rtt, measuredMinimumMilliseconds = ordered[0],
                measuredMedianMilliseconds = ordered[ordered.Length / 2], measuredP95Milliseconds = ordered[(int)Math.Ceiling(ordered.Length * .95) - 1],
                measuredMaximumMilliseconds = ordered[^1], verifiedDatagrams = 24, verifiedBytes, isolatedFlows = snapshot.TotalFlows,
                valid = snapshot.QualificationValid,
            };
        }
        finally
        {
            stop.Cancel();
            foreach (var client in clients) client.Dispose();
            await running;
        }
    }

    private static async Task<object> VerifyLossAsync(string root)
    {
        await using var echo = new EchoServer();
        string stem = Path.Combine(root, "loss");
        var options = Options(echo.Port, stem, 20, 25);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var relay = new UdpImpairmentRelay(options);
        Task running = relay.RunAsync(stop.Token);
        using var client = Client(options.Front);
        const int count = 400;
        var expected = Enumerable.Range(0, count).Select(index => Payload(index, 256)).ToArray();
        var received = new HashSet<int>();
        using var receiveStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        Task receiver = ReceiveAsync();
        try
        {
            for (int index = 0; index < count; index++)
            {
                await client.SendAsync(expected[index], SocketFlags.None, stop.Token);
                if (index % 20 == 19) await Task.Delay(5, stop.Token);
            }
            await Task.Delay(700, stop.Token);
            receiveStop.Cancel();
            await receiver;
            await File.WriteAllTextAsync(options.StopFile, "loss complete");
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            var snapshot = relay.Snapshot();
            Check(snapshot.QualificationValid && snapshot.ClientToServer.ReceivedDatagrams == count &&
                snapshot.ClientToServer.SimulatedLostDatagrams > 0 && snapshot.ServerToClient.SimulatedLostDatagrams > 0 &&
                snapshot.ClientToServer.ForwardedDatagrams == snapshot.ServerToClient.ReceivedDatagrams &&
                snapshot.ServerToClient.ForwardedDatagrams == received.Count && received.Count is > 0 and < count,
                "Loss was not independently applied and accounted in both actual UDP directions.");
            return new
            {
                test = "actual-bidirectional-loss", sent = count, received = received.Count,
                upstreamDropped = snapshot.ClientToServer.SimulatedLostDatagrams,
                downstreamDropped = snapshot.ServerToClient.SimulatedLostDatagrams,
                preservedPayloadHashes = true, seed = options.Seed, valid = snapshot.QualificationValid,
            };
        }
        finally
        {
            receiveStop.Cancel();
            stop.Cancel();
            await receiver;
            await running;
        }

        async Task ReceiveAsync()
        {
            byte[] buffer = new byte[65535];
            try
            {
                while (!receiveStop.IsCancellationRequested)
                {
                    int length = await client.ReceiveAsync(buffer, SocketFlags.None, receiveStop.Token);
                    int index = length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(buffer) : -1;
                    Check(index >= 0 && index < count && expected[index].Length == length &&
                        SHA256.HashData(expected[index]).AsSpan().SequenceEqual(SHA256.HashData(buffer.AsSpan(0, length))) && received.Add(index),
                        "Loss echo was changed, duplicated, or routed incorrectly.");
                }
            }
            catch (OperationCanceledException) when (receiveStop.IsCancellationRequested) { }
        }
    }

    private static async Task<object> VerifyBoundAsync(string root, bool flowLimit)
    {
        await using var echo = new EchoServer();
        string name = flowLimit ? "flow-bound" : "queue-bound";
        var options = Options(echo.Port, Path.Combine(root, name), 1000, 0) with
        {
            MaximumFlows = flowLimit ? 1 : 16,
            MaximumQueueBytes = flowLimit ? 64 * 1024 * 1024 : 1024,
        };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var relay = new UdpImpairmentRelay(options);
        Task running = relay.RunAsync(stop.Token);
        using var first = Client(options.Front);
        using var second = Client(options.Front);
        await first.SendAsync(Payload(1, flowLimit ? 1 : 2048), SocketFlags.None, stop.Token);
        if (flowLimit) await second.SendAsync(Payload(2, 1), SocketFlags.None, stop.Token);
        await running.WaitAsync(TimeSpan.FromSeconds(3));
        var snapshot = relay.Snapshot();
        Check(!snapshot.QualificationValid && snapshot.Failures.Any(failure => failure.Contains(flowLimit ? "Flow limit" : "queue limit", StringComparison.Ordinal)),
            name + " did not fail closed.");
        Check(snapshot.PeakQueuedBytes <= options.MaximumQueueBytes, "Queue exceeded its memory limit.");
        return new { test = name, failedClosed = true, failures = snapshot.Failures, peakQueuedBytes = snapshot.PeakQueuedBytes };
    }

    private static async Task<object> VerifyRetiredClientsAsync(string root)
    {
        await using var echo = new EchoServer();
        var options = Options(echo.Port, Path.Combine(root, "retired-clients"), 50, 0);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var relay = new UdpImpairmentRelay(options);
        Task running = relay.RunAsync(stop.Token);
        using var current = Client(options.Front);
        var retiredClients = Enumerable.Range(0, 8).Select(_ => Client(options.Front)).ToArray();
        try
        {
            // Echoes target ports that have already closed, generating actual
            // Windows ICMP port-unreachable. A replacement client must survive.
            for (int index = 0; index < 8; index++)
            {
                await retiredClients[index].SendAsync(Payload(index, 256), SocketFlags.None, stop.Token);
                retiredClients[index].Dispose();
            }
            await Task.Delay(250, stop.Token);
            for (int index = 0; index < 5; index++)
            {
                byte[] payload = Payload(9000 + index, 1024);
                await current.SendAsync(payload, SocketFlags.None, stop.Token);
                byte[] response = new byte[65535];
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                int length = await current.ReceiveAsync(response, SocketFlags.None, timeout.Token);
                Check(length == payload.Length && SHA256.HashData(payload).AsSpan().SequenceEqual(SHA256.HashData(response.AsSpan(0, length))),
                    "Replacement flow failed after retired UDP ports.");
            }
            await File.WriteAllTextAsync(options.StopFile, "retired clients complete");
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            var snapshot = relay.Snapshot();
            Check(snapshot.QualificationValid && snapshot.TotalFlows == 9 && snapshot.ClientToServer.ForwardedDatagrams == 13 &&
                snapshot.ServerToClient.ForwardedDatagrams == 13, "Retired UDP ports invalidated the relay or disrupted the replacement flow.");
            return new
            {
                test = "retired-client-icmp", retiredPorts = 8, verifiedReplacementEchoes = 5,
                snapshot.WindowsUdpConnectionResetNotificationsSuppressed,
                observedTransientIcmpNotifications = snapshot.ClientToServer.TransientIcmpNotifications + snapshot.ServerToClient.TransientIcmpNotifications,
                valid = snapshot.QualificationValid,
            };
        }
        finally
        {
            stop.Cancel();
            foreach (var retired in retiredClients) retired.Dispose();
            await running;
        }
    }

    private static RelayOptions Options(int port, string stem, int rtt, int loss) =>
        new(new IPEndPoint(IPAddress.Parse("127.0.0.2"), port), new IPEndPoint(IPAddress.Loopback, port),
            rtt, loss, 379019, stem + ".json", stem + ".stop");

    private static Socket Client(IPEndPoint front)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ReceiveBufferSize = 4 * 1024 * 1024 };
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.Connect(front);
        return socket;
    }

    private static byte[] Payload(int index, int size)
    {
        byte[] bytes = new byte[size];
        new Random(index).NextBytes(bytes);
        if (size >= 4) BinaryPrimitives.WriteInt32LittleEndian(bytes, index);
        return bytes;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class EchoServer : IAsyncDisposable
    {
        private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ExclusiveAddressUse = true,
            ReceiveBufferSize = 4 * 1024 * 1024,
            SendBufferSize = 4 * 1024 * 1024,
        };
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _running;
        internal int Port => ((IPEndPoint)_socket.LocalEndPoint!).Port;

        internal EchoServer()
        {
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _running = LoopAsync();
        }

        private async Task LoopAsync()
        {
            byte[] buffer = new byte[65535];
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var received = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), _stop.Token);
                    await _socket.SendToAsync(buffer.AsMemory(0, received.ReceivedBytes), SocketFlags.None, received.RemoteEndPoint, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _running;
            _socket.Dispose();
            _stop.Dispose();
        }
    }
}
