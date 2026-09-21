using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anomaly.Client.Api.Input;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class InputPersistenceTests
{
    [Fact]
    public async Task Saved_binding_override_survives_disposal_and_registration_replacement()
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Anomaly.Input.Tests"));
        string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
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
            start.Environment["ANOMALY_INPUT_TEST_ASSEMBLY_DIRECTORY"] = Path.GetDirectoryName(typeof(InputRegistry).Assembly.Location);
            start.Environment["ANOMALY_INPUT_TEST_BASE_DIRECTORY"] = root;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated input probe.");
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                using var killTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(killTimeout.Token);
                throw new TimeoutException("The isolated input probe exceeded 30 seconds.");
            }
            string output = await outputTask;
            string errors = await errorTask;
            Assert.True(process.ExitCode == 0, $"Input probe failed: {errors}");
            Assert.True(string.IsNullOrWhiteSpace(errors), $"Input probe wrote errors: {errors}");
            using JsonDocument result = JsonDocument.Parse(output);
            Assert.Equal("G", result.RootElement.GetProperty("FirstKey").GetString());
            Assert.Equal("G", result.RootElement.GetProperty("ReplacementKey").GetString());
            Assert.True(result.RootElement.GetProperty("Removed").GetBoolean());
            Assert.True(result.RootElement.GetProperty("FileUnchanged").GetBoolean());
        }
        finally
        {
            string resolved = Path.GetFullPath(root);
            if (string.Equals(Path.GetDirectoryName(resolved), parent, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
                Directory.Delete(resolved, recursive: true);
        }
    }

    private const string ProbeScript = """
        $ErrorActionPreference = 'Stop'
        $assemblyDirectory = $env:ANOMALY_INPUT_TEST_ASSEMBLY_DIRECTORY
        $melon = [System.Reflection.Assembly]::LoadFrom([IO.Path]::Combine($assemblyDirectory, 'MelonLoader.dll'))
        $configType = $melon.GetType('MelonLoader.LoaderConfig', $true)
        $config = $configType.GetProperty('Current').GetValue($null)
        $core = $configType.GetProperty('Loader').GetValue($config)
        $baseSetter = $core.GetType().GetProperty('BaseDirectory').GetSetMethod($true)
        [void]$baseSetter.Invoke($core, @($env:ANOMALY_INPUT_TEST_BASE_DIRECTORY))
        [void][System.Reflection.Assembly]::LoadFrom([IO.Path]::Combine($assemblyDirectory, 'Anomaly.Client.Api.dll'))
        [void][System.Reflection.Assembly]::LoadFrom([IO.Path]::Combine($assemblyDirectory, 'UnityEngine.CoreModule.dll'))
        $dataDirectory = [IO.Path]::Combine($env:ANOMALY_INPUT_TEST_BASE_DIRECTORY, 'UserData', 'Anomaly')
        [void][IO.Directory]::CreateDirectory($dataDirectory)
        $bindingsPath = [IO.Path]::Combine($dataDirectory, 'bindings.json')
        $saved = '{"tests.input.persisted":{"key":"G","modifiers":[]}}'
        [IO.File]::WriteAllText($bindingsPath, $saved)
        $defaultKey = [Anomaly.Client.Api.Input.InputKey]::new([UnityEngine.KeyCode]::F, [Anomaly.Client.Api.Input.InputModifiers]::None)
        $definition = [Anomaly.Client.Api.Input.InputBindingDefinition]::new('tests.input.persisted', 'tests.input.persisted.name', $defaultKey, '')
        $first = [Anomaly.Client.Api.Input.InputRegistry]::Register($definition)
        $firstKey = [Anomaly.Client.Api.Input.InputRegistry]::GetResolvedKey($definition.Id).Key.ToString()
        $first.Dispose()
        $removed = $null -eq [Anomaly.Client.Api.Input.InputRegistry]::GetBinding($definition.Id)
        $replacement = [Anomaly.Client.Api.Input.InputRegistry]::Register($definition)
        $first.Dispose()
        $replacementKey = [Anomaly.Client.Api.Input.InputRegistry]::GetResolvedKey($definition.Id).Key.ToString()
        $replacement.Dispose()
        [ordered]@{
            FirstKey = $firstKey
            ReplacementKey = $replacementKey
            Removed = $removed
            FileUnchanged = [IO.File]::ReadAllText($bindingsPath) -ceq $saved
        } | ConvertTo-Json -Compress
        """;
}
