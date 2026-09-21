using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anomaly.Client.Api.Features;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class InstanceStoragePathTests
{
    [Fact]
    public async Task Real_paths_are_instance_local_persist_across_restarts_and_never_fall_back()
    {
        using var temporary = new TemporaryDirectory();

        string firstRoot = Path.Combine(temporary.RootPath, "first game");
        ProbeResult first = await ProbeAsync(firstRoot);
        AssertLayout(first, firstRoot);
        Assert.Equal(new[] { "Anomaly/Overrides/README.md" },
            Directory.EnumerateFiles(first.UserDataDirectory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(first.UserDataDirectory, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal));

        string anomalyRoot = first.Paths[nameof(Paths.AnomalyPath)];
        string sentinel = Path.Combine(anomalyRoot, "existing-state.json");
        string readme = Path.Combine(first.Paths[nameof(Paths.OverridesPath)], "README.md");
        Assert.True(File.Exists(readme));
        Assert.Contains("Anomaly Asset Overrides", File.ReadAllText(readme));

        const string existingState = "{\"instance\":\"first\"}";
        const string customReadme = "Player-maintained override instructions.\n";
        File.WriteAllText(sentinel, existingState);
        File.WriteAllText(readme, customReadme);

        ProbeResult restarted = await ProbeAsync(firstRoot);
        AssertLayout(restarted, firstRoot);
        Assert.Equal(existingState, File.ReadAllText(sentinel));
        Assert.Equal(customReadme, File.ReadAllText(readme));

        string secondRoot = Path.Combine(temporary.RootPath, "second game");
        ProbeResult second = await ProbeAsync(secondRoot);
        AssertLayout(second, secondRoot);
        Assert.NotEqual(anomalyRoot, second.Paths[nameof(Paths.AnomalyPath)]);
        Assert.False(File.Exists(Path.Combine(second.Paths[nameof(Paths.AnomalyPath)], "existing-state.json")));
        Assert.Contains("Anomaly Asset Overrides", File.ReadAllText(
            Path.Combine(second.Paths[nameof(Paths.OverridesPath)], "README.md")));
        Assert.Equal(existingState, File.ReadAllText(sentinel));
        Assert.Equal(customReadme, File.ReadAllText(readme));

        // A file in place of the loader root makes UserData unavailable on
        // every platform, including when tests run with elevated privileges.
        string unavailableRoot = Path.Combine(temporary.RootPath, "not-a-directory");
        File.WriteAllText(unavailableRoot, "keep this file");
        ProbeResult unavailable = await ProbeAsync(unavailableRoot);
        Assert.False(unavailable.Success);
        Assert.Contains(unavailable.ErrorType, new[]
        {
            typeof(IOException).FullName,
            typeof(DirectoryNotFoundException).FullName,
        });
        Assert.Equal("keep this file", File.ReadAllText(unavailableRoot));
        Assert.False(Directory.Exists(Path.Combine(unavailableRoot, "UserData")));
    }

    private static void AssertLayout(ProbeResult result, string instanceRoot)
    {
        Assert.True(result.Success, $"Paths initialization failed: {result.ErrorType}");
        string userData = Path.Combine(instanceRoot, "UserData");
        string anomaly = Path.Combine(userData, "Anomaly");
        var expected = new Dictionary<string, string>
        {
            [nameof(Paths.ApplicationDataPath)] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            [nameof(Paths.AnomalyPath)] = anomaly,
            [nameof(Paths.AnomalyUserDataPath)] = anomaly,
            [nameof(Paths.FileCachePath)] = Path.Combine(anomaly, "FileCache"),
            [nameof(Paths.CrashReportsPath)] = Path.Combine(anomaly, "CrashReports"),
            [nameof(Paths.SkinsPath)] = Path.Combine(userData, "Skins"),
            [nameof(Paths.OverridesPath)] = Path.Combine(anomaly, "Overrides"),
            [nameof(Paths.AudioOverridesPath)] = Path.Combine(anomaly, "Overrides", "AudioClip"),
        };

        Assert.Equal(userData, result.UserDataDirectory);
        Assert.Equal(expected.Count, result.Paths.Count);
        foreach ((string name, string path) in expected)
            Assert.Equal(path, result.Paths[name]);

        // Cache and report writers create their own directories on demand.
        Assert.Equal(new[] { "Anomaly", "Anomaly/Overrides", "Anomaly/Overrides/AudioClip", "Skins" },
            Directory.EnumerateDirectories(userData, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(userData, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal));
    }

    private static async Task<ProbeResult> ProbeAsync(string instanceRoot)
    {
        // Loading Paths in this process would freeze its root for the other
        // tests. Each child sets MelonLoader's actual configuration before any
        // MelonEnvironment or Paths static initialization takes place.
        string assemblyDirectory = Path.GetDirectoryName(typeof(Paths).Assembly.Location)!;
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(ProbeScript)));
        start.Environment["ANOMALY_PATH_TEST_ASSEMBLY_DIRECTORY"] = assemblyDirectory;
        start.Environment["ANOMALY_PATH_TEST_BASE_DIRECTORY"] = instanceRoot;

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the storage path probe; PowerShell 7 (pwsh) is required.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            using var killTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(killTimeout.Token);
            throw new TimeoutException("The isolated storage path probe exceeded 30 seconds.");
        }

        string output = await outputTask;
        string errors = await errorTask;
        Assert.True(process.ExitCode == 0, $"Storage path probe exited {process.ExitCode}: {errors}");
        Assert.True(string.IsNullOrWhiteSpace(errors), $"Storage path probe wrote errors: {errors}");
        return JsonSerializer.Deserialize<ProbeResult>(output)
            ?? throw new InvalidOperationException("The storage path probe returned no result.");
    }

    private sealed class ProbeResult
    {
        public bool Success { get; set; }
        public string ErrorType { get; set; } = string.Empty;
        public string UserDataDirectory { get; set; } = string.Empty;
        public Dictionary<string, string> Paths { get; set; } = new();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Anomaly.StoragePath.Tests"));

        internal TemporaryDirectory()
        {
            RootPath = Path.Combine(_parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        internal string RootPath { get; }

        public void Dispose()
        {
            string resolved = Path.GetFullPath(RootPath);
            string relative = Path.GetRelativePath(_parent, resolved);
            if (relative.Length != 32 || relative.Contains(Path.DirectorySeparatorChar) ||
                !Guid.TryParseExact(relative, "N", out _) ||
                !string.Equals(Path.GetDirectoryName(resolved), _parent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to clean up an unexpected storage test directory.");
            if (Directory.Exists(resolved))
                Directory.Delete(resolved, recursive: true);
        }
    }

    private const string ProbeScript = """
        $ErrorActionPreference = 'Stop'
        try {
            $assemblyDirectory = $env:ANOMALY_PATH_TEST_ASSEMBLY_DIRECTORY
            $melon = [System.Reflection.Assembly]::LoadFrom([IO.Path]::Combine($assemblyDirectory, 'MelonLoader.dll'))
            $configType = $melon.GetType('MelonLoader.LoaderConfig', $true)
            $config = $configType.GetProperty('Current').GetValue($null)
            $core = $configType.GetProperty('Loader').GetValue($config)
            $baseSetter = $core.GetType().GetProperty('BaseDirectory').GetSetMethod($true)
            [void]$baseSetter.Invoke($core, @($env:ANOMALY_PATH_TEST_BASE_DIRECTORY))
            $environment = $melon.GetType('MelonLoader.Utils.MelonEnvironment', $true)
            $userData = $environment.GetProperty('UserDataDirectory').GetValue($null)
            $api = [System.Reflection.Assembly]::LoadFrom([IO.Path]::Combine($assemblyDirectory, 'Anomaly.Client.Api.dll'))
            $paths = $api.GetType('Anomaly.Client.Api.Features.Paths', $true)
            $values = [ordered]@{}
            foreach ($name in @('ApplicationDataPath', 'AnomalyPath', 'AnomalyUserDataPath', 'FileCachePath', 'CrashReportsPath', 'SkinsPath', 'OverridesPath', 'AudioOverridesPath')) {
                $values[$name] = $paths.GetProperty($name).GetValue($null)
            }
            [ordered]@{ Success = $true; UserDataDirectory = $userData; Paths = $values } | ConvertTo-Json -Compress
        } catch {
            [ordered]@{ Success = $false; ErrorType = $_.Exception.GetBaseException().GetType().FullName } | ConvertTo-Json -Compress
        }
        """;
}
