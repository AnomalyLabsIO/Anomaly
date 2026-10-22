[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$LoaderDirectory,
    [Parameter(Mandatory)][string]$ClientAssembly,
    [Parameter(Mandatory)][string]$BootstrapAssembly,
    [string]$Configuration = 'Debug',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) {
    & dotnet build (Join-Path $PSScriptRoot 'Anomaly.Bootstrap.StartupProbe.csproj') -c $Configuration -p:DeployLocal=false
    if ($LASTEXITCODE -ne 0) { throw 'Startup probe build failed.' }
}

$probeAssembly = Join-Path $PSScriptRoot "bin/$Configuration/net6.0/Anomaly.Bootstrap.StartupProbe.dll"
foreach ($mode in @('missing', 'failed', 'fatal')) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @($probeAssembly, $LoaderDirectory, $ClientAssembly, $BootstrapAssembly)) {
        $startInfo.ArgumentList.Add([System.IO.Path]::GetFullPath($argument))
    }
    $startInfo.ArgumentList.Add($mode)
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            throw "Startup probe '$mode' exceeded 30 seconds."
        }
        $output = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 78 -or $errors.Length -ne 0 -or
            -not $output.Contains('PASS Client assembly loaded and Core constructed before Bootstrap validation') -or
            -not $output.Contains('PASS blocking Bootstrap warning requested before exit:')) {
            throw "Startup probe '$mode' failed (exit $($process.ExitCode)).`n$output`n$errors"
        }
        if (($mode -eq 'missing' -and -not $output.Contains('PASS absent Bootstrap raises catchable FileNotFoundException')) -or
            ($mode -eq 'failed' -and -not $output.Contains('PASS failed Bootstrap returns false')) -or
            ($mode -eq 'fatal' -and
                (-not $output.Contains('PASS worker guard failure is latched without displaying a warning') -or
                 -not $output.Contains('PASS invoking real main-thread heartbeat; expected process exit code 78') -or
                 -not $output.Contains('Worker guard failure injected by startup probe.'))) -or
            ($mode -ne 'fatal' -and
                -not $output.Contains('PASS invoking real early lifecycle; expected process exit code 78'))) {
            throw "Startup probe '$mode' did not exercise the expected guard boundary.`n$output"
        }
        Write-Output "PASS $mode Bootstrap: guard rejects, main thread requests warning, required action exits 78."
    }
    finally {
        $process.Dispose()
    }
}
