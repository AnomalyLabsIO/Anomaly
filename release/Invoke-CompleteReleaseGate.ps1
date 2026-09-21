[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,
    [string] $OutputDirectory = '',
    [string] $SourceCommit = '',
    [long] $SourceDateEpoch = -1
)

$ErrorActionPreference = 'Stop'
$gateRepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $gateRepositoryRoot ("artifacts/release-{0}" -f $Version)
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

$currentSourceCommit = (& git -C $gateRepositoryRoot rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the current source commit.' }
if ([string]::IsNullOrWhiteSpace($SourceCommit)) {
    $SourceCommit = $currentSourceCommit
} elseif ($SourceCommit.ToLowerInvariant() -ne $currentSourceCommit) {
    throw 'SourceCommit must identify the currently checked-out commit.'
}
$sourceStatus = @(& git -C $gateRepositoryRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Unable to verify the release source worktree.' }
if ($sourceStatus.Count -ne 0) {
    throw 'The complete release gate requires a clean worktree before it can emit commit-bound receipts.'
}
if ($SourceDateEpoch -lt 0) {
    $epochText = (& git -C $gateRepositoryRoot show -s --format=%ct $SourceCommit).Trim()
    if ($LASTEXITCODE -ne 0 -or -not [long]::TryParse($epochText, [ref]$SourceDateEpoch)) {
        throw 'Unable to resolve SOURCE_DATE_EPOCH from the source commit.'
    }
}

$reproOutput = Join-Path ([System.IO.Path]::GetTempPath()) `
    ("anomaly-release-repro-{0}" -f [guid]::NewGuid().ToString('N'))
$evidenceWorkingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) `
    ("anomaly-release-evidence-{0}" -f [guid]::NewGuid().ToString('N'))
$evidenceLogDirectory = Join-Path $evidenceWorkingDirectory 'logs'
$evidenceTestDirectory = Join-Path $evidenceWorkingDirectory 'test-results'
[void](New-Item -ItemType Directory -Path $evidenceLogDirectory)
[void](New-Item -ItemType Directory -Path $evidenceTestDirectory)
$gateResults = [Collections.Generic.List[object]]::new()

function Invoke-RecordedGate {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $Command,
        [Parameter(Mandatory = $true)] [scriptblock] $Action
    )

    $sequence = $gateResults.Count + 1
    $logName = ("{0:D2}-{1}.log" -f $sequence, $Name)
    $logPath = Join-Path $evidenceLogDirectory $logName
    $startedUtc = [DateTimeOffset]::UtcNow
    $status = 'failed'
    [IO.File]::WriteAllText(
        $logPath,
        ("Command: {0}{1}" -f $Command, [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
    try {
        & $Action *>&1 | Tee-Object -FilePath $logPath -Append
        $status = 'passed'
    }
    catch {
        $_ | Out-String | Out-File -LiteralPath $logPath -Append -Encoding utf8
        throw
    }
    finally {
        $gateResults.Add([ordered]@{
            name = $Name
            status = $status
            command = $Command
            startedUtc = $startedUtc.ToString('O')
            completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
            logPath = ("evidence/logs/{0}" -f $logName)
            resultPaths = @()
        })
    }
}

function Get-NativeToolVersion {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [scriptblock] $Action
    )

    $value = @(& $Action)
    if ($LASTEXITCODE -ne 0 -or $value.Count -eq 0) {
        throw ("Unable to resolve the {0} version." -f $Name)
    }
    return (($value | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ }) -join '; ')
}

try {
    Invoke-RecordedGate 'repository-secret-scan' './release/Test-RepositorySecrets.ps1' {
        & (Join-Path $PSScriptRoot 'Test-RepositorySecrets.ps1')
    }
    Invoke-RecordedGate 'compile-list-verification' './release/Verify-CompileLists.ps1' {
        & (Join-Path $PSScriptRoot 'Verify-CompileLists.ps1')
    }

    Invoke-RecordedGate 'release-build' `
        'dotnet build Anomaly.slnx -t:Rebuild -c Release -p:DeployLocal=false -p:ContinuousIntegrationBuild=true -warnaserror --nologo' {
        # Local incremental outputs may have been compiled with different CI/path-map
        # properties. Rebuild both candidates so the comparison uses identical inputs.
        & dotnet build (Join-Path $gateRepositoryRoot 'Anomaly.slnx') `
            -t:Rebuild -c Release -p:DeployLocal=false -p:ContinuousIntegrationBuild=true `
            -warnaserror --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Release/x64 solution build failed.' }
    }

    Invoke-RecordedGate 'core-tests' `
        'dotnet test <each test project in Anomaly.slnx> -c Release -p:DeployLocal=false --no-build --nologo --logger <project>.trx --results-directory <evidence>/test-results' {
        [xml] $solution = Get-Content -Raw -LiteralPath (Join-Path $gateRepositoryRoot 'Anomaly.slnx')
        $testProjects = @($solution.Solution.Project | ForEach-Object { $_.Path } | Where-Object {
            $projectPath = Join-Path $gateRepositoryRoot $_
            (Test-Path -LiteralPath $projectPath -PathType Leaf) -and
                (Select-String -LiteralPath $projectPath -SimpleMatch 'Microsoft.NET.Test.Sdk' -Quiet)
        } | Sort-Object)
        if ($testProjects.Count -eq 0) { throw 'Anomaly.slnx contains no test projects.' }
        foreach ($testProject in $testProjects) {
            $testResultName = [IO.Path]::GetFileNameWithoutExtension($testProject) + '.trx'
            & dotnet test (Join-Path $gateRepositoryRoot $testProject) `
                -c Release -p:DeployLocal=false --no-build --nologo `
                --logger ("trx;LogFileName={0}" -f $testResultName) `
                --results-directory $evidenceTestDirectory
            if ($LASTEXITCODE -ne 0) { throw ("Core test project failed: {0}" -f $testProject) }
        }
    }
    $testResults = @(Get-ChildItem -LiteralPath $evidenceTestDirectory -File -Filter '*.trx')
    if ($testResults.Count -eq 0) { throw 'Core test suite produced no TRX result files.' }
    $gateResults[$gateResults.Count - 1].resultPaths = @($testResults | Sort-Object Name | ForEach-Object {
        "evidence/test-results/$($_.Name)"
    })

    Invoke-RecordedGate 'public-api-baseline' `
        'dotnet run --project tools/PublicApiBaseline/PublicApiBaseline.csproj -c Release -- <api-dll> <baseline>' {
        & dotnet run --project (Join-Path $gateRepositoryRoot 'tools/PublicApiBaseline/PublicApiBaseline.csproj') `
            -c Release -- `
            (Join-Path $gateRepositoryRoot 'Anomaly.Client.Api/bin/Release/net6.0/Anomaly.Client.Api.dll') `
            (Join-Path $gateRepositoryRoot 'Anomaly.Client.Api/PublicAPI.Shipped.txt')
        if ($LASTEXITCODE -ne 0) { throw 'Shipped public API baseline verification failed.' }
    }

    Invoke-RecordedGate 'public-api-fixtures' `
        'dotnet run --project tools/PublicApiBaseline/PublicApiBaseline.csproj -c Release -- --self-test' {
        & dotnet run --project (Join-Path $gateRepositoryRoot 'tools/PublicApiBaseline/PublicApiBaseline.csproj') `
            -c Release -- --self-test
        if ($LASTEXITCODE -ne 0) { throw 'Public API compatibility fixtures failed.' }
    }

    Invoke-RecordedGate 'release-tool-tests' './release/Test-ReleaseTools.ps1' {
        & (Join-Path $PSScriptRoot 'Test-ReleaseTools.ps1')
    }

    Invoke-RecordedGate 'primary-package-build' `
        './release/Build-Release.ps1 -Version <version> -OutputDirectory <output> -SourceCommit <commit> -SourceDateEpoch <epoch> -SkipBuild' {
        & (Join-Path $PSScriptRoot 'Build-Release.ps1') `
            -Version $Version -OutputDirectory $OutputDirectory `
            -SourceCommit $SourceCommit -SourceDateEpoch $SourceDateEpoch -SkipBuild
    }
    Invoke-RecordedGate 'package-closure' './release/Test-PackageClosure.ps1 -ReleaseOutput <output>' {
        & (Join-Path $PSScriptRoot 'Test-PackageClosure.ps1') -ReleaseOutput $OutputDirectory
    }
    Invoke-RecordedGate 'sdk-compile-closure' './release/Test-SdkCompileClosure.ps1 -ReleaseOutput <output>' {
        & (Join-Path $PSScriptRoot 'Test-SdkCompileClosure.ps1') -ReleaseOutput $OutputDirectory
    }

    # Rebuild, rather than merely re-stage, so the second package proves both
    # deterministic compiler output and deterministic archive construction.
    Invoke-RecordedGate 'reproducibility-rebuild' `
        'dotnet build Anomaly.slnx -t:Rebuild -c Release -p:DeployLocal=false -p:ContinuousIntegrationBuild=true -warnaserror --nologo' {
        & dotnet build (Join-Path $gateRepositoryRoot 'Anomaly.slnx') `
            -t:Rebuild -c Release -p:DeployLocal=false -p:ContinuousIntegrationBuild=true `
            -warnaserror --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Second clean Release/x64 build failed.' }
    }

    Invoke-RecordedGate 'reproducibility-package-build' `
        './release/Build-Release.ps1 -Version <version> -OutputDirectory <temporary-output> -SourceCommit <commit> -SourceDateEpoch <epoch> -SkipBuild' {
        & (Join-Path $PSScriptRoot 'Build-Release.ps1') `
            -Version $Version -OutputDirectory $reproOutput `
            -SourceCommit $SourceCommit -SourceDateEpoch $SourceDateEpoch -SkipBuild
    }
    Invoke-RecordedGate 'reproducibility-comparison' `
        './release/Compare-ReleaseOutputs.ps1 -ReferenceOutput <output> -CandidateOutput <temporary-output>' {
        & (Join-Path $PSScriptRoot 'Compare-ReleaseOutputs.ps1') `
            -ReferenceOutput $OutputDirectory -CandidateOutput $reproOutput
    }

    # Evidence is deliberately materialized only after the deterministic output
    # comparison. It therefore cannot hide or perturb a package reproducibility
    # failure, while the finalized receipt still binds every retained log/result.
    $evidenceOutputDirectory = Join-Path $OutputDirectory 'evidence'
    if (Test-Path -LiteralPath $evidenceOutputDirectory) {
        throw 'Release output unexpectedly contains evidence before finalization.'
    }
    [void](New-Item -ItemType Directory -Path (Join-Path $evidenceOutputDirectory 'logs') -Force)
    [void](New-Item -ItemType Directory -Path (Join-Path $evidenceOutputDirectory 'test-results') -Force)
    Get-ChildItem -LiteralPath $evidenceLogDirectory -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $evidenceOutputDirectory 'logs')
    }
    Get-ChildItem -LiteralPath $evidenceTestDirectory -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $evidenceOutputDirectory 'test-results')
    }

    $toolVersions = @(
        [ordered]@{ name = 'dotnet-runtime'; version = Get-NativeToolVersion 'dotnet runtimes' { & dotnet --list-runtimes } },
        [ordered]@{ name = 'dotnet-sdk'; version = Get-NativeToolVersion 'dotnet SDK' { & dotnet --version } },
        [ordered]@{ name = 'git'; version = Get-NativeToolVersion 'Git' { & git --version } },
        [ordered]@{ name = 'powershell'; version = $PSVersionTable.PSVersion.ToString() },
        [ordered]@{ name = 'ripgrep'; version = Get-NativeToolVersion 'ripgrep' { & rg --version | Select-Object -First 1 } }
    )
    $gateSummary = [ordered]@{
        schemaVersion = 1
        completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        summaryPath = 'evidence/gate-summary.json'
        toolVersions = $toolVersions
        gates = @($gateResults)
    }
    $summaryPath = Join-Path $evidenceOutputDirectory 'gate-summary.json'
    [IO.File]::WriteAllText(
        $summaryPath,
        ($gateSummary | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))

    & dotnet run --project (Join-Path $gateRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release --no-build -- finalize-gate-evidence $OutputDirectory $summaryPath
    if ($LASTEXITCODE -ne 0) { throw 'Complete-gate receipt finalization failed.' }
    & (Join-Path $PSScriptRoot 'Test-ReleaseEvidence.ps1') -ReleaseOutput $OutputDirectory

    $global:LASTEXITCODE = 0
    Write-Host ("Complete unsigned release gate and evidence verification passed: {0}" -f $OutputDirectory)
}
finally {
    $resolvedReproOutput = [System.IO.Path]::GetFullPath($reproOutput)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedReproOutput.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedReproOutput).StartsWith(
            'anomaly-release-repro-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedReproOutput -Recurse -Force -ErrorAction SilentlyContinue
    }
    $resolvedEvidenceWorkingDirectory = [System.IO.Path]::GetFullPath($evidenceWorkingDirectory)
    if ($resolvedEvidenceWorkingDirectory.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedEvidenceWorkingDirectory).StartsWith(
            'anomaly-release-evidence-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedEvidenceWorkingDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
