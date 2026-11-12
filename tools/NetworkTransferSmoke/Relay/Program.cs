using System.Globalization;
using System.Net;
using System.Text.Json;

namespace NetworkImpairmentRelay;

internal static class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static async Task<int> Main(string[] args)
    {
        RelayOptions? options = null;
        try
        {
            var values = Parse(args);
            if (values.ContainsKey("self-test"))
                return await RelaySelfTest.RunAsync(Required(values, "output"));

            options = new RelayOptions(
                ParseEndpoint(Required(values, "front")),
                ParseEndpoint(Required(values, "upstream")),
                Number(values, "rtt-ms", 0),
                Number(values, "loss-percent", 0),
                Integer(values, "seed", 129734),
                Path.GetFullPath(Required(values, "stats")),
                Path.GetFullPath(Required(values, "stop-file")),
                Integer(values, "max-flows", 16),
                checked(Integer(values, "max-queue-mib", 64) * 1024L * 1024L),
                Integer(values, "idle-seconds", 120));
            options.Validate();
            if (File.Exists(options.StopFile))
                throw new ArgumentException("Remove the previous stop file before starting a new relay run.");

            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
            await using var relay = new UdpImpairmentRelay(options);
            Console.WriteLine($"Loopback relay {options.Front} -> {options.Upstream}; added RTT {options.RttMilliseconds} ms; independent loss {options.LossPercent}% each direction.");
            await relay.RunAsync(shutdown.Token);
            var result = relay.Snapshot();
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return result.QualificationValid ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            if (options != null)
                await WriteJsonAsync(options.StatsPath, new
                {
                    qualificationValid = false,
                    failure = exception.ToString(),
                    front = options.Front.ToString(),
                    upstream = options.Upstream.ToString(),
                    options.RttMilliseconds,
                    options.LossPercent,
                    options.Seed,
                });
            else
                Console.Error.WriteLine("Usage: --front 127.0.0.2:29149 --upstream 127.0.0.1:29149 --rtt-ms 50 --loss-percent 0 --seed 129734 --stats <json> --stop-file <path>\nSelf-test: --self-test --output <directory>");
            return 1;
        }
    }

    internal static async Task WriteJsonAsync(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "self-test", "output", "front", "upstream", "rtt-ms", "loss-percent", "seed", "stats", "stop-file",
            "max-flows", "max-queue-mib", "idle-seconds",
        };
        for (int index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Arguments must use --name value.");
            string key = args[index][2..];
            if (!allowed.Contains(key)) throw new ArgumentException("Unknown option: " + key);
            string value = key == "self-test" ? "true" : ++index < args.Length ? args[index] : throw new ArgumentException("Missing value: " + key);
            if (!result.TryAdd(key, value)) throw new ArgumentException("Repeated option: " + key);
        }
        return result;
    }

    private static string Required(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) ? value : throw new ArgumentException("Missing --" + key);

    private static int Integer(Dictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out string? value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    private static double Number(Dictionary<string, string> values, string key, double fallback) =>
        values.TryGetValue(key, out string? value) ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;

    private static IPEndPoint ParseEndpoint(string value) => IPEndPoint.TryParse(value, out var endpoint)
        ? endpoint : throw new ArgumentException("Invalid numeric IP endpoint: " + value);
}

internal sealed record RelayOptions(IPEndPoint Front, IPEndPoint Upstream, double RttMilliseconds, double LossPercent,
    int Seed, string StatsPath, string StopFile, int MaximumFlows = 16, long MaximumQueueBytes = 64 * 1024 * 1024,
    int IdleSeconds = 120)
{
    internal const int MaximumQueuedDatagrams = 65536;

    internal void Validate()
    {
        if (!Front.Address.Equals(IPAddress.Parse("127.0.0.2")) || !Upstream.Address.Equals(IPAddress.Loopback) ||
            Front.Port <= 0 || Upstream.Port != Front.Port)
            throw new ArgumentException("Only front 127.0.0.2 and upstream 127.0.0.1 using the same nonzero UDP port are allowed.");
        if (!double.IsFinite(RttMilliseconds) || RttMilliseconds < 0 || RttMilliseconds > 2000 ||
            !double.IsFinite(LossPercent) || LossPercent < 0 || LossPercent > 50 ||
            MaximumFlows < 1 || MaximumFlows > 16 || MaximumQueueBytes < 1024 || MaximumQueueBytes > 64 * 1024 * 1024 ||
            IdleSeconds < 2 || IdleSeconds > 600)
            throw new ArgumentException("Relay limits are invalid (RTT 0..2000 ms, loss 0..50%, flows 1..16, queue 1 KiB..64 MiB, idle 2..600 s).");
        if (Path.GetFullPath(StatsPath).Equals(Path.GetFullPath(StopFile), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Stats and stop paths must differ.");
    }
}
