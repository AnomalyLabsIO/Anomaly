using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anomaly.InstallerCore.Trust;
using Anomaly.Launcher.Models;
using Anomaly.Launcher.Services;
using Anomaly.Launcher.Services.Install;
using Anomaly.Launcher.Services.Loadouts;
using Anomaly.Launcher.Services.Platform;
using Anomaly.Lite.Core;
using Anomaly.Lite.Core.Provisioning;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly List<object> Spans = new();
    private static string OwnedRoot = "";
    private static bool Incremental;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 3 || args[0] is not ("materialize" or "lite-pinned"))
        {
            Console.Error.WriteLine("materialize <local-loadout-source> <new-output> [pairs=5] | lite-pinned <idle-lite-game-source> <new-output> [pairs=5]");
            return 2;
        }
        string source = Path.GetFullPath(args[1]);
        OwnedRoot = Path.GetFullPath(args[2]).TrimEnd(Path.DirectorySeparatorChar);
        if (Directory.Exists(OwnedRoot) || File.Exists(OwnedRoot) || OwnedRoot.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use a new output outside the source directory.");
        int pairs = args.Length > 3 ? int.Parse(args[3]) : 5;
        if (pairs is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(pairs));
        Directory.CreateDirectory(OwnedRoot);
        Write("launcher-bench-staging.json", new { schemaVersion = 1, source, destination = OwnedRoot, noGameLaunch = true });
        if (args[0] == "materialize") await Materialize(source, pairs);
        else await LitePinned(source, pairs);
        return 0;
    }

    private static async Task Materialize(string source, int pairs)
    {
        if (!File.Exists(Path.Combine(source, "Mods", "Anomaly.Client.dll"))) throw new InvalidDataException("Local core source is required; official trust is never bypassed for this benchmark.");
        string root = Path.Combine(OwnedRoot, "private-loadout");
        var paths = new PathService(Path.Combine(root, "launcher"), Path.Combine(root, "appdata"));
        paths.SetGameRoot(root);
        string local = paths.GetLoadoutLocalDir("bench");
        foreach (string folder in new[] { "Mods", "Plugins", "UserLibs", "UserData/Skins", "UserData/i18n" })
            CopyTree(Path.Combine(source, folder), Path.Combine(local, folder));
        Directory.CreateDirectory(paths.GetGameDir("latest"));
        var network = new NetworkProbe(false);
        using var http = new HttpClient(network);
        using var platform = new PlatformApiClient(new PlatformOptions(), http);
        var materializer = new LoadoutMaterializer(paths, new ModPackageCache(paths, platform), platform);
        var loadout = new Loadout { Name = "bench", GameVersion = "latest", TrackAnomalyClient = true };
        string runtime = paths.GetLoadoutRuntimeDir("bench");
        var runs = new List<object>();
        // JIT/initial materialization warmup is deliberately excluded.
        await materializer.MaterializeAsync(loadout);
        for (int pair = 1; pair <= pairs; pair++)
        foreach (string mode in pair % 2 == 1 ? new[] { "baseline", "incremental" } : new[] { "incremental", "baseline" })
        {
            Incremental = mode == "incremental";
            Spans.Clear();
            string sentinel = Path.Combine(runtime, "UserData", "Anomaly", "FileCache", "synthetic-cache.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            File.WriteAllText(sentinel, "synthetic fixture; no real identity imported");
            long start = Stopwatch.GetTimestamp();
            string[] warnings;
            if (!Incremental)
            {
                var progress = new StageProgress();
                var result = await materializer.MaterializeAsync(loadout, progress);
                progress.Finish();
                warnings = result.Warnings.ToArray();
            }
            else
            {
                foreach (string folder in new[] { "Mods", "Plugins", "UserLibs" })
                {
                    long stage = Stopwatch.GetTimestamp();
                    string destination = Path.Combine(runtime, folder);
                    ResetExecutableTree(destination);
                    CopyChanged(Path.Combine(local, folder), destination, false, new List<string>());
                    Spans.Add(new { name = "incremental." + folder, elapsedMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds });
                }
                // Only copied skins/i18n assets exist in this deliberately local-only fixture.
                foreach (string folder in new[] { "UserData/Skins", "UserData/i18n" }) CopyChanged(Path.Combine(local, folder), Path.Combine(runtime, folder), false, new List<string>());
                new ModTrustService(runtime).UseDevelopmentPolicy();
                warnings = Array.Empty<string>();
            }
            double materializeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            var links = LoadoutLinkManager.ActivateWithDetails(paths.GetGameDir("latest"), runtime, "bench");
            double linkMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            LoadoutLinkManager.Deactivate(paths.GetGameDir("latest"));
            bool contentMatches = new[] { "Mods", "Plugins", "UserLibs" }.All(folder => Manifest(Path.Combine(local, folder)).SequenceEqual(Manifest(Path.Combine(runtime, folder))));
            runs.Add(new { pair, mode, materializeMs, linkMs, cachePreserved = File.Exists(sentinel), contentMatches, warnings, links, spans = Spans.ToArray() });
        }
        Write("report.json", new
        {
            schemaVersion = 1, lane = "local development loadout; no catalog or game process", pairs,
            implementation = AssemblyIdentity(typeof(LoadoutMaterializer).Assembly), networkRequests = network.Requests,
            candidate = "Harness-only local-development experiment: reconcile executable trees, copy only differing bytes, preserve client-owned Anomaly data; production sibling source untouched.",
            limitations = "Installed launcher binary is tested and fingerprinted; this is launcher preparation time, not menu readiness. UserData preservation is an intentional correctness change, not byte-for-byte baseline behavior.", runs
        });
        Console.WriteLine(Path.Combine(OwnedRoot, "report.json"));
    }

    private static async Task LitePinned(string source, int pairs)
    {
        if (File.Exists(Path.Combine(source, ".anomaly-lite", "session.json")) || File.Exists(Path.Combine(source, "version.dll")))
            throw new InvalidOperationException("Lite seed must be idle/restored with no active journal or loader bootstrap.");
        string game = Path.Combine(OwnedRoot, "private-lite-game");
        string cache = Path.Combine(game, ".anomaly-lite");
        Directory.CreateDirectory(cache);
        foreach (string relative in new[] { "SCPSL.exe", "GameAssembly.dll", "UnityPlayer.dll", "SCPSL_Data/il2cpp_data/Metadata/global-metadata.dat", "SL-AC.dll", "SCPSL_Data/globalgamemanagers" })
        {
            string file = Path.Combine(source, relative);
            if (!File.Exists(file)) { if (relative == "SL-AC.dll") continue; throw new FileNotFoundException(relative); }
            CopyFile(file, Path.Combine(game, relative));
        }
        CopyTree(Path.Combine(source, "MelonLoader"), Path.Combine(game, "MelonLoader"));
        foreach (string relative in new[] { "prepared", "tools", "resources" }) CopyTree(Path.Combine(source, ".anomaly-lite", relative), Path.Combine(cache, relative));
        foreach (string relative in new[] { "runtime-owner.json", "settings.json", "downloads/MelonLoader-0.7.2.x64.zip" }) CopyFile(Path.Combine(source, ".anomaly-lite", relative), Path.Combine(cache, relative));
        foreach (string manifest in Directory.GetFiles(Path.Combine(cache, "prepared"), "prepared.json", SearchOption.AllDirectories))
        {
            // Only absolute clone locations change; all original content hashes remain mandatory.
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))!;
            var session = node["session"]!;
            session["preparedDirectory"] = Path.Combine(Path.GetDirectoryName(manifest)!, "payloads");
            session["melonLoaderArchive"] = Path.Combine(cache, "downloads", "MelonLoader-0.7.2.x64.zip");
            var oldAdapter = session["adapterDirectory"]?.GetValue<string>();
            if (oldAdapter != null && oldAdapter.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                session["adapterDirectory"] = Path.Combine(game, Path.GetRelativePath(source, oldAdapter));
            File.WriteAllText(manifest, node.ToJsonString(Json));
        }
        var settings = FileOps.ReadJson<LiteSettings>(Path.Combine(cache, "settings.json"));
        var network = new NetworkProbe(false);
        using var http = new HttpClient(network) { Timeout = TimeSpan.FromSeconds(30) };
        var log = new List<string>();
        var preparation = new PreparationService(log.Add, http);
        // All metadata requests fail immediately, forcing the genuine existing-cache path.
        // A cache miss throws from that path before any patching or generation.
        var runs = new List<object>();
        var warmupRuns = new List<object>();
        for (int pair = 0; pair <= pairs; pair++)
        foreach (string mode in pair % 2 == 1 ? new[] { "baseline", "pinned" } : new[] { "pinned", "baseline" })
        {
            log.Clear();
            int requests = network.Requests;
            long start = Stopwatch.GetTimestamp();
            bool success = false;
            string? failure = null;
            string? fingerprint = null;
            try
            {
                PreparedSession session;
                if (mode == "baseline") session = await preparation.PrepareAsync(game, cache, settings, CancellationToken.None);
                else
                {
                    var runtime = HostRuntime.Resolve();
                    await (Task)PrivateMethod("ProbeRuntimeAsync").Invoke(preparation, new object[] { runtime.DotnetRoot, CancellationToken.None })!;
                    var inputs = (List<FileIdentity>)PrivateMethod("CaptureInputs").Invoke(null, new object[] { game })!;
                    string inputKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(inputs, JsonDefaults.Options))));
                    session = (PreparedSession?)PrivateMethod("FindOfflineSession").Invoke(preparation, new object[] { cache, inputKey, settings })
                        ?? throw new InvalidDataException("No complete validated preparation exists for the exact cloned inputs.");
                }
                RuntimeInstallation.InstallPersistentFiles(game, session, log.Add);
                fingerprint = session.Fingerprint;
                success = true;
            }
            catch (Exception ex) { failure = ex.GetBaseException().Message; }
            var row = new { pair, mode, success, failure, fingerprint, elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds, networkRequests = network.Requests - requests, logs = log.ToArray() };
            if (pair == 0) warmupRuns.Add(row); else runs.Add(row);
        }
        Write("report.json", new
        {
            schemaVersion = 1, lane = "Lite preparation only; no activation, journal creation or game launch", pairs, onlineUpdates = false,
            networkLabel = "immediate network-unavailable fixture; does not measure Internet waits",
            candidate = "Pinned path still probes runtime, hashes exact inputs, validates complete preparation/payload/archive/runtime, then revalidates installed runtime. Only optional update discovery is omitted.", warmupRuns, runs
        });
        Console.WriteLine(Path.Combine(OwnedRoot, "report.json"));
    }

    private static MethodInfo PrivateMethod(string name) => typeof(PreparationService).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) ?? throw new MissingMethodException(name);
    private static bool ResetExecutableTree(string path)
    {
        RequireOwned(path);
        if (!Incremental) return true;
        string local = path.Replace(Path.DirectorySeparatorChar + "runtime" + Path.DirectorySeparatorChar, Path.DirectorySeparatorChar + "local" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        Directory.CreateDirectory(path);
        foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            if (!File.Exists(Path.Combine(local, Path.GetRelativePath(path, file)))) { RequireOwned(file); File.Delete(file); }
        return false;
    }
    private static bool CopyChanged(string src, string dest, bool mergeOnly, List<string> warnings)
    {
        RequireOwned(dest);
        if (!Incremental) return true;
        if (!Directory.Exists(src)) return false;
        foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(dest, Path.GetRelativePath(src, file));
            if (File.Exists(target) && (mergeOnly || (new FileInfo(file).Length == new FileInfo(target).Length && Hash(file) == Hash(target)))) continue;
            CopyFile(file, target);
        }
        return false;
    }
    private static object AssemblyIdentity(Assembly assembly) => new { path = assembly.Location, sha256 = Hash(assembly.Location), version = assembly.GetName().Version?.ToString(), lastWriteUtc = File.GetLastWriteTimeUtc(assembly.Location) };
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string[] Manifest(string root) => !Directory.Exists(root) ? Array.Empty<string>() : Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(root, file) + ":" + Hash(file)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    private static void RequireOwned(string path)
    {
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(OwnedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Mutation escaped benchmark root.");
    }
    private static void CopyFile(string source, string target)
    {
        RequireOwned(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, true);
    }
    private static void CopyTree(string source, string target)
    {
        if (!Directory.Exists(source)) return;
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Refusing linked source subtree: " + source);
        foreach (string file in Directory.GetFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Refusing source link: " + file);
            CopyFile(file, Path.Combine(target, Path.GetFileName(file)));
        }
        foreach (string directory in Directory.GetDirectories(source)) CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
    private static void Write(string relative, object value) => File.WriteAllText(Path.Combine(OwnedRoot, relative), JsonSerializer.Serialize(value, Json));
    private sealed class StageProgress : IProgress<LoadoutMaterializer.MaterializeProgress>
    {
        private long _start = Stopwatch.GetTimestamp();
        private string _stage = "materialize.start";
        public void Report(LoadoutMaterializer.MaterializeProgress value)
        {
            Spans.Add(new { name = _stage, elapsedMs = Stopwatch.GetElapsedTime(_start).TotalMilliseconds });
            _stage = value.Stage;
            _start = Stopwatch.GetTimestamp();
        }
        internal void Finish() => Spans.Add(new { name = _stage, elapsedMs = Stopwatch.GetElapsedTime(_start).TotalMilliseconds });
    }
    private sealed class NetworkProbe(bool online) : HttpMessageHandler
    {
        private readonly HttpClient _live = new() { Timeout = TimeSpan.FromSeconds(30) };
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (!online) throw new HttpRequestException("Benchmark immediate-unavailable network fixture.");
            return _live.SendAsync(request, cancellationToken);
        }
        protected override void Dispose(bool disposing) { if (disposing) _live.Dispose(); base.Dispose(disposing); }
    }
}
