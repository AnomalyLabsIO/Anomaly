[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ReleaseOutput
)

$ErrorActionPreference = 'Stop'
$closureRepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ReleaseOutput = [System.IO.Path]::GetFullPath($ReleaseOutput)
if (-not (Test-Path -LiteralPath $ReleaseOutput -PathType Container)) {
    throw "Release output does not exist: $ReleaseOutput"
}

$closureTempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) `
    ("anomaly-package-closure-test-{0}" -f [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $closureTempDirectory)
try {
    & dotnet run --project (Join-Path $closureRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release -- audit-closure $ReleaseOutput
    if ($LASTEXITCODE -ne 0) { throw 'Valid package dependency closure was rejected.' }

    Copy-Item -LiteralPath (Join-Path $ReleaseOutput 'package-layout.json') `
        -Destination (Join-Path $closureTempDirectory 'package-layout.json')
    Copy-Item -LiteralPath (Join-Path $ReleaseOutput 'dependency-inventory.json') `
        -Destination (Join-Path $closureTempDirectory 'dependency-inventory.json')
    Copy-Item -LiteralPath (Join-Path $ReleaseOutput 'payloads') `
        -Destination (Join-Path $closureTempDirectory 'payloads') -Recurse

    $layoutPath = Join-Path $closureTempDirectory 'package-layout.json'
    $layout = Get-Content -Raw -LiteralPath $layoutPath | ConvertFrom-Json
    $clientPackage = $layout.packages | Where-Object packageId -eq 'anomaly-client'
    $clientPackage.entries = @($clientPackage.entries | Where-Object path -ne 'UserLibs/Mono.Cecil.Rocks.dll')
    [IO.File]::WriteAllBytes(
        $layoutPath,
        [Text.UTF8Encoding]::new($false).GetBytes(($layout | ConvertTo-Json -Depth 16)))
    Remove-Item -LiteralPath `
        (Join-Path $closureTempDirectory 'payloads/client-runtime/UserLibs/Mono.Cecil.Rocks.dll') -Force

    & dotnet run --project (Join-Path $closureRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release --no-build -- audit-closure $closureTempDirectory
    if ($LASTEXITCODE -eq 0) { throw 'Missing runtime dependency unexpectedly passed closure validation.' }

    $global:LASTEXITCODE = 0
    Write-Host 'Positive and missing-dependency runtime closure gates passed.'
}
finally {
    $resolvedClosureTemp = [System.IO.Path]::GetFullPath($closureTempDirectory)
    $resolvedSystemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedClosureTemp.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedClosureTemp).StartsWith(
            'anomaly-package-closure-test-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedClosureTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
