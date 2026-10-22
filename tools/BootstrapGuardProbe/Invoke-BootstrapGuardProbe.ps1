[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$LoaderDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [ValidateSet('strict','allow','preexisting','stocktamper')]
    [string[]]$Modes = @('strict','allow','preexisting','stocktamper'),
    [string]$Configuration = 'Debug',
    [switch]$SkipBuild,
    [switch]$CheckFailureHandling
)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) {
    & dotnet build (Join-Path $PSScriptRoot 'Anomaly.Bootstrap.GuardProbe.csproj') -c $Configuration -p:DeployLocal=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Guard probe build failed.' }
}
$probe = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "bin/$Configuration/net6.0/Anomaly.Bootstrap.GuardProbe.dll"))
$libraries = [IO.Path]::GetFullPath($LoaderDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Use a new probe evidence directory.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$probeModes = if ($CheckFailureHandling) { @('handled-failure') } else { $Modes }
foreach ($mode in $probeModes) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add($probe)
    if (-not $CheckFailureHandling) {
        $startInfo.ArgumentList.Add($libraries)
        $startInfo.ArgumentList.Add($mode)
    }
    $started = [DateTimeOffset]::UtcNow
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit(30000)
        if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $evidence "$mode.stdout.log"), $stdout)
        [IO.File]::WriteAllText((Join-Path $evidence "$mode.stderr.log"), $stderr)
        [ordered]@{mode=$mode;processId=$process.Id;startedUtc=$started.ToString('O');endedUtc=[DateTimeOffset]::UtcNow.ToString('O');exitCode=$process.ExitCode;timedOut=$timedOut} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence "$mode.json")
        $expected = if ($CheckFailureHandling) { 1 } else { 0 }
        $marker = if ($CheckFailureHandling) { $stderr.Contains('FAIL Bootstrap guard probe: System.ArgumentException: Usage:') } else { $stdout.Contains('PASS ' + $mode + ' flags=') }
        if ($timedOut -or $process.ExitCode -ne $expected -or -not $marker) {
            throw "Guard probe '$mode' failed (exit $($process.ExitCode), timeout $timedOut). Evidence: $evidence`n$stdout`n$stderr"
        }
        Write-Output "PASS $mode (exit $($process.ExitCode)); evidence: $evidence"
    }
    finally { $process.Dispose() }
}
