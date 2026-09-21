[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ReleaseOutput
)

$ErrorActionPreference = 'Stop'
$evidenceRepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ReleaseOutput = [System.IO.Path]::GetFullPath($ReleaseOutput)
if (-not (Test-Path -LiteralPath $ReleaseOutput -PathType Container)) {
    throw "Release output does not exist: $ReleaseOutput"
}

& dotnet run --project (Join-Path $evidenceRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
    -c Release --no-build -- verify $ReleaseOutput
if ($LASTEXITCODE -ne 0) { throw 'ReleasePackaging rejected the finalized release evidence.' }

$receiptPath = Join-Path $ReleaseOutput 'release-receipt.json'
$receipt = Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json
if ($receipt.schemaVersion -ne 3 -or
    $receipt.sourceCommit -notmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$' -or
    $receipt.dependencyInventorySha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'Receipt source/dependency identity is missing or malformed.'
}
foreach ($package in @($receipt.packages)) {
    if ($package.sha256 -notmatch '^[0-9a-f]{64}$' -or $package.sizeBytes -le 0) {
        throw ("Receipt package hash is missing or malformed: {0}" -f $package.packageId)
    }
}

if ($receipt.signing.state -ne 'unsigned' -or $null -ne $receipt.signing.keyId -or
    @($receipt.signing.trustedKeyIds).Count -ne 0 -or @($receipt.signing.files).Count -ne 0) {
    throw 'The complete unsigned gate must record unsigned signing state and an empty trustedKeyIds list.'
}

$gateEvidence = $receipt.gateEvidence
if ($null -eq $gateEvidence -or $gateEvidence.schemaVersion -ne 1) {
    throw 'The complete unsigned gate receipt has no finalized gate evidence.'
}
$requiredTools = @('dotnet-runtime', 'dotnet-sdk', 'git', 'powershell', 'ripgrep')
$toolNames = @($gateEvidence.toolVersions | ForEach-Object { $_.name })
foreach ($requiredTool in $requiredTools) {
    if ($requiredTool -notin $toolNames) { throw "Gate evidence is missing $requiredTool version information." }
}

$gates = @($gateEvidence.gates)
if ($gates.Count -eq 0 -or @($gates | Where-Object { $_.status -ne 'passed' }).Count -ne 0) {
    throw 'Gate evidence contains no results or contains a non-passing result.'
}
$resultPaths = @($gates | ForEach-Object { @($_.resultPaths) })
if (@($resultPaths | Where-Object { $_ -like '*.trx' }).Count -eq 0) {
    throw 'Gate evidence contains no TRX test results.'
}

$rootWithSeparator = $ReleaseOutput.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$recordedFiles = @($gateEvidence.files | Sort-Object path)
$actualFiles = @(Get-ChildItem -LiteralPath (Join-Path $ReleaseOutput 'evidence') -File -Recurse | ForEach-Object {
    [pscustomobject]@{
        path = $_.FullName.Substring($rootWithSeparator.Length).Replace('\', '/')
        sizeBytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
} | Sort-Object path)
if ($recordedFiles.Count -ne $actualFiles.Count) {
    throw 'Recorded and materialized gate evidence file counts differ.'
}
for ($index = 0; $index -lt $recordedFiles.Count; $index++) {
    $recorded = $recordedFiles[$index]
    $actual = $actualFiles[$index]
    if ($recorded.path -ne $actual.path -or
        $recorded.sizeBytes -ne $actual.sizeBytes -or
        $recorded.sha256 -ne $actual.sha256) {
        throw ("Gate evidence file does not match its receipt: {0}" -f $actual.path)
    }
}

$tamperDirectory = Join-Path ([System.IO.Path]::GetTempPath()) `
    ("anomaly-release-evidence-tamper-{0}" -f [guid]::NewGuid().ToString('N'))
try {
    Copy-Item -LiteralPath $ReleaseOutput -Destination $tamperDirectory -Recurse
    $tamperLog = Get-ChildItem -LiteralPath (Join-Path $tamperDirectory 'evidence/logs') `
        -File -Filter '*.log' | Select-Object -First 1
    if ($null -eq $tamperLog) { throw 'No command log is available for the evidence tamper probe.' }
    [IO.File]::AppendAllText($tamperLog.FullName, 'tamper-probe', [Text.UTF8Encoding]::new($false))
    $tamperVerificationOutput = @(& dotnet run --project `
        (Join-Path $evidenceRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release --no-build -- verify $tamperDirectory 2>&1)
    if ($LASTEXITCODE -eq 0) { throw 'Tampered gate evidence unexpectedly passed receipt verification.' }

    $global:LASTEXITCODE = 0
    Write-Host ("Release source, dependency, package, tool, gate, log, TRX, and tamper evidence passed: {0}" -f $ReleaseOutput)
}
finally {
    $resolvedTamperDirectory = [System.IO.Path]::GetFullPath($tamperDirectory)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedTamperDirectory.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTamperDirectory).StartsWith(
            'anomaly-release-evidence-tamper-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTamperDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
