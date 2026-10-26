using System.Net;
using System.Net.Sockets;

namespace Anomaly.ScriptSandboxPrototype;

/// <summary>Receives only connections to a task-owned loopback port; no external network calls.</summary>
internal sealed class LoopbackWitness : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Task _acceptLoop;
    private int _connections;

    public LoopbackWitness()
    {
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/sandbox-probe";
        _acceptLoop = AcceptLoop();
    }

    public string Url { get; }
    public int Connections => Volatile.Read(ref _connections);
    public bool IsListening => !_acceptLoop.IsCompleted;

    private async Task AcceptLoop()
    {
        try
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync();
                Interlocked.Increment(ref _connections);
                // Answer immediately: an accidentally exposed HTTP client must not hang
                // waiting for a witness which itself waits for the worker to exit.
                try
                {
                    await client.GetStream().WriteAsync(System.Text.Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"));
                }
                catch (System.IO.IOException) { }
            }
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        await _acceptLoop;
    }
}
