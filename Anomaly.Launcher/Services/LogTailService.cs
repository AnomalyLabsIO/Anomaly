namespace Anomaly.Launcher.Services;

public class LogTailService
{
    private readonly PathService _paths;
    private CancellationTokenSource? _cts;
    private long _lastPosition;

    public event EventHandler<string>? LineReceived;

    public LogTailService(PathService paths)
    {
        _paths = paths;
    }

    public void Start()
    {
        Stop();
        _lastPosition = 0;
        _cts = new CancellationTokenSource();
        _ = PollAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task PollAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct);
                ReadNewLines();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore read errors, try again next tick
            }
        }
    }

    private void ReadNewLines()
    {
        var logPath = _paths.GameLogPath;
        if (!File.Exists(logPath))
            return;

        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Handle log rotation (file was truncated/recreated)
        if (stream.Length < _lastPosition)
            _lastPosition = 0;

        if (stream.Length == _lastPosition)
            return;

        stream.Seek(_lastPosition, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            LineReceived?.Invoke(this, line);
        }

        _lastPosition = stream.Position;
    }
}
