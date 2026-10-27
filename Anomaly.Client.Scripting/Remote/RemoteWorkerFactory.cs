using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Anomaly.Client.Scripting.Remote.Isolation;
using Anomaly.Client.Scripting.Remote.Protocol;

namespace Anomaly.Client.Scripting.Remote;

/// <summary>Availability is earned by a real sandbox launch/probe, never by a platform-name check.</summary>
internal static class RemoteWorkerFactory {
    private sealed record Payload(string Root, string Dotnet, string UserData, IReadOnlyDictionary<string, string> Hashes);
    private sealed record Qualification(Payload? Payload, string Reason);
    private static readonly string[] Names = { "Anomaly.Client.ScriptWorker.dll", "Anomaly.Client.ScriptWorker.deps.json", "Anomaly.Client.ScriptWorker.runtimeconfig.json", "Anomaly.MoonSharp.Interpreter.dll" };
    private static Task<Qualification>? _qualification;
    private static Payload? _payload;
    private static int _state;
    private static string _reason = "Remote worker isolation qualification has not completed.";
    internal static bool IsQualified => Volatile.Read(ref _state) == 2;
    internal static bool IsQualifying => Volatile.Read(ref _state) == 1;
    internal static string DisabledReason => IsQualified ? "" : Volatile.Read(ref _reason);

    internal static void BeginQualification(string userDataRoot) {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return;
        string root = Path.GetDirectoryName(typeof(RemoteWorkerFactory).Assembly.Location) ?? "";
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        _qualification = Task.Run(() => QualifyAsync(root, runtime, Path.GetFullPath(userDataRoot)));
    }
    internal static void Tick() {
        var task = _qualification;
        if (Volatile.Read(ref _state) != 1 || task == null || !task.IsCompleted) return;
        var result = task.GetAwaiter().GetResult();
        _payload = result.Payload;
        Volatile.Write(ref _reason, result.Reason);
        Volatile.Write(ref _state, result.Payload == null ? 3 : 2);
        _qualification = null;
    }
    internal static Task<WorkerSandboxProcess> StartWorkerAsync() {
        var payload = _payload;
        if (!IsQualified || payload == null) throw new NotSupportedException(DisabledReason);
        return Task.Run(() => Start(payload));
    }

    private static async Task<Qualification> QualifyAsync(string root, string runtime, string userData) {
        try {
            if (!OperatingSystem.IsWindows()) return new(null, "Remote Lua requires the qualified Windows AppContainer/job worker; this OS is unsupported.");
            var hashes = ReadIdentity();
            string dotnet = FindDotnet(runtime);
            var payload = new Payload(root, dotnet, userData, hashes);
            Directory.CreateDirectory(userData);
            string probeDirectory = Path.Combine(userData, "WorkerIsolation");
            Directory.CreateDirectory(probeDirectory);
            string sentinel = Path.Combine(probeDirectory, "probe-" + Guid.NewGuid().ToString("N") + ".bin");
            byte[] expected = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(sentinel, expected);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try {
                listener.Start(1);
                using var channel = new RemoteWorkerChannel(Start(payload));
                var evidence = channel.Process.Evidence;
                if (!evidence.AppContainer || !evidence.LessPrivilegedAppContainer || evidence.LoopbackExempt || evidence.NetworkCapabilities != 0 || evidence.ActiveProcessLimit != 1 ||
                    evidence.ProcessMemoryLimitBytes != 256L * 1024 * 1024 || evidence.JobMemoryLimitBytes != 256L * 1024 * 1024 ||
                    evidence.CpuRatePercent != 10) throw new InvalidDataException("The worker's actual token/job limits did not match the required isolation policy.");
                var greeting = await channel.ReadAsync().WaitAsync(TimeSpan.FromSeconds(16)).ConfigureAwait(false);
                if (greeting.Kind != FrameKind.Ready || greeting.Data.Required("version").RequireInteger(1, 1) != 1)
                    throw new InvalidDataException("The isolated worker did not complete its protocol handshake.");
                await CheckLoopbackControl(listener).ConfigureAwait(false);
                var observedConnection = listener.AcceptTcpClientAsync();
                var boundary = Record(("mode", WireValue.String("Boundary")), ("sentinel", WireValue.String(sentinel)),
                    ("endpoint", WireValue.String("127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port)), ("executable", WireValue.String(dotnet)));
                var proof = await Probe(channel, 1, boundary).ConfigureAwait(false);
                bool denied = proof.Required("networkDenied").RequireBoolean();
                bool timedOut = proof.Required("networkTimedOut").RequireBoolean();
                bool unavailable = proof.Required("networkUnavailable").RequireBoolean();
                if (!proof.Required("fileDenied").RequireBoolean() || (!denied && !timedOut && !unavailable) || !proof.Required("childDenied").RequireBoolean())
                    throw new InvalidDataException("The worker did not prove protected-file, network and child-process denial.");
                // WFP can silently drop denied traffic. A timeout alone proves nothing:
                // both controls use this same retained endpoint, and an accepted worker
                // connection cannot impersonate the parent's undisclosed control nonce.
                await CheckLoopbackControl(listener, observedConnection).ConfigureAwait(false);
                if (!File.ReadAllBytes(sentinel).AsSpan().SequenceEqual(expected)) throw new InvalidDataException("The worker modified its protected host sentinel.");
                var memory = await Probe(channel, 2, Record(("mode", WireValue.String("Memory")))).ConfigureAwait(false);
                if (!memory.Required("memoryDenied").RequireBoolean() || memory.Required("allocatedBytes").RequireInteger(0, 512L * 1024 * 1024) < 128L * 1024 * 1024)
                    throw new InvalidDataException("The worker could not demonstrate native allocation denial under its OS job ceiling.");
                long peak = evidence.PeakProcessMemoryBytes;
                if (peak < 128L * 1024 * 1024 || peak > evidence.ProcessMemoryLimitBytes)
                    throw new InvalidDataException("The native-memory probe's observed job peak was outside its verified process ceiling.");
                return new(payload, "");
            } finally { listener.Stop(); File.Delete(sentinel); }
        } catch (Exception error) {
            string message = error.Message;
            if (message.Length > 512) message = message.Substring(0, 512);
            return new(null, "Remote worker isolation qualification failed: " + message);
        }
    }
    private static async Task<WireValue> Probe(RemoteWorkerChannel channel, long command, WireValue data) {
        channel.Arm(TimeSpan.FromSeconds(15));
        if (!channel.TrySend(new RemoteFrame(FrameKind.Probe, 0, command, 0, data))) throw new IOException(channel.Fault);
        var result = await channel.ReadAsync().WaitAsync(TimeSpan.FromSeconds(16)).ConfigureAwait(false);
        if (result.Kind != FrameKind.Completed || result.Session != 0 || result.Command != command || result.Data.Required("status").RequireString(32) != "Completed")
            throw new InvalidDataException("The isolated worker boundary probe failed: " + result.Data.Optional("error").Text);
        return result.Data.Required("value");
    }
    private static async Task CheckLoopbackControl(TcpListener listener, Task<TcpClient>? pending = null) {
        var accepted = pending ?? listener.AcceptTcpClientAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        await client.ConnectAsync(IPAddress.Loopback, endpoint.Port).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        await client.GetStream().WriteAsync(nonce).ConfigureAwait(false);
        using var server = await accepted.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var received = new byte[nonce.Length];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        int offset = 0;
        while (offset < received.Length) {
            int count = await server.GetStream().ReadAsync(received.AsMemory(offset), timeout.Token).ConfigureAwait(false);
            if (count == 0) throw new InvalidDataException("An unexpected process connected during the isolation probe.");
            offset += count;
        }
        if (!CryptographicOperations.FixedTimeEquals(nonce, received) || listener.Pending())
            throw new InvalidDataException("The network isolation positive control was intercepted.");
    }

    private static IReadOnlyDictionary<string, string> ReadIdentity() {
        using var stream = typeof(RemoteWorkerFactory).Assembly.GetManifestResourceStream("Anomaly.Client.Scripting.WorkerIdentity.json")
            ?? throw new FileNotFoundException("The trusted worker payload identity manifest is absent.");
        if (stream.Length > 4096) throw new InvalidDataException("Worker payload identity manifest is oversized.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = document.RootElement.GetProperty("files");
        if (files.GetArrayLength() != Names.Length) throw new InvalidDataException("Incomplete worker payload identity manifest.");
        foreach (var entry in files.EnumerateArray()) {
            string name = entry.GetProperty("name").GetString() ?? "";
            string hash = entry.GetProperty("sha256").GetString() ?? "";
            if (!Names.Contains(name, StringComparer.Ordinal) || hash.Length != 64 || !hash.All(Uri.IsHexDigit) || !result.TryAdd(name, hash))
                throw new InvalidDataException("Invalid worker payload identity record.");
        }
        return result;
    }
    private static WorkerSandboxProcess Start(Payload payload) {
        var leases = new List<FileStream>(Names.Length);
        WorkerSandboxProcess? process = null;
        try {
            var files = new string[Names.Length];
            using var hash = SHA256.Create();
            for (int i = 0; i < Names.Length; i++) {
                string file = Path.Combine(payload.Root, Names[i]);
                files[i] = file;
                var lease = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                leases.Add(lease);
                if (lease.Length is <= 0 or > 16 * 1024 * 1024 || !string.Equals(Convert.ToHexString(hash.ComputeHash(lease)), payload.Hashes[Names[i]], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Trusted worker payload hash mismatch: " + Names[i]);
            }
            process = WorkerSandboxProcess.Start(new WorkerSandboxOptions(payload.Dotnet, files[0], files, new[] { payload.UserData }));
            foreach (var lease in leases) process.Retain(lease);
            leases.Clear();
            return process;
        } catch { process?.Dispose(); throw; }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }
    private static string FindDotnet(string runtime) {
        // Prefer the already loaded machine runtime's host; never resolve an executable via PATH/current directory.
        var directory = new DirectoryInfo(runtime);
        for (int i = 0; directory != null && i < 6; i++, directory = directory.Parent) {
            string candidate = Path.Combine(directory.FullName, "dotnet.exe");
            if (File.Exists(candidate) && Directory.Exists(Path.Combine(directory.FullName, "shared", "Microsoft.NETCore.App"))) return candidate;
        }
        string machine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        if (File.Exists(machine)) return machine;
        throw new FileNotFoundException("A trusted x64 dotnet host with the .NET 6 runtime is required; no PATH or in-process fallback is permitted.");
    }
    internal static WireValue Record(params (string Name, WireValue Value)[] fields) {
        var result = new Dictionary<string, WireValue>(fields.Length, StringComparer.Ordinal);
        foreach (var field in fields) result.Add(field.Name, field.Value);
        return WireValue.Object("", result);
    }
}
