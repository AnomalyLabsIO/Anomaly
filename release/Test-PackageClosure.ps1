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
    $clientPackage.entries = @($clientPackage.entries | Where-Object path -ne 'UserLibs/AssetsTools.NET.dll')
    [IO.File]::WriteAllBytes(
        $layoutPath,
        [Text.UTF8Encoding]::new($false).GetBytes(($layout | ConvertTo-Json -Depth 16)))
    Remove-Item -LiteralPath `
        (Join-Path $closureTempDirectory 'payloads/client-runtime/UserLibs/AssetsTools.NET.dll') -Force

    & dotnet run --project (Join-Path $closureRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release --no-build -- audit-closure $closureTempDirectory
    if ($LASTEXITCODE -eq 0) { throw 'Missing runtime dependency unexpectedly passed closure validation.' }

    Copy-Item -LiteralPath (Join-Path $ReleaseOutput 'package-layout.json') -Destination $layoutPath -Force
    $layout = Get-Content -Raw -LiteralPath $layoutPath | ConvertFrom-Json
    $clientPackage = $layout.packages | Where-Object packageId -eq 'anomaly-client'
    ($clientPackage.runtimeClosure.hostProvidedAssemblies | Where-Object name -eq '0Harmony').assemblyVersion = '2.16.0.0'
    [IO.File]::WriteAllBytes(
        $layoutPath,
        [Text.UTF8Encoding]::new($false).GetBytes(($layout | ConvertTo-Json -Depth 16)))
    Copy-Item -LiteralPath `
        (Join-Path $ReleaseOutput 'payloads/client-runtime/UserLibs/AssetsTools.NET.dll') `
        -Destination (Join-Path $closureTempDirectory 'payloads/client-runtime/UserLibs/AssetsTools.NET.dll')

    & dotnet run --project (Join-Path $closureRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release --no-build -- audit-closure $closureTempDirectory
    if ($LASTEXITCODE -eq 0) { throw 'Mismatched host assembly version unexpectedly passed closure validation.' }

    Copy-Item -LiteralPath (Join-Path $ReleaseOutput 'package-layout.json') -Destination $layoutPath -Force
    $inventoryPath = Join-Path $closureTempDirectory 'dependency-inventory.json'
    $inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
    $resolvedHarmony = @($inventory.resolvedInputs | Where-Object {
        $_.project -eq 'Anomaly.Client' -and $_.name -eq '0Harmony.dll'
    })
    if ($resolvedHarmony.Count -ne 1) { throw 'Expected one captured Anomaly.Client Harmony input.' }
    $resolvedHarmony[0].assemblyVersion = '2.16.0.0'
    [IO.File]::WriteAllBytes(
        $inventoryPath,
        [Text.UTF8Encoding]::new($false).GetBytes(($inventory | ConvertTo-Json -Depth 16)))

    & dotnet run --project (Join-Path $closureRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release --no-build -- audit-closure $closureTempDirectory
    if ($LASTEXITCODE -eq 0) { throw 'Mismatched resolved Harmony version unexpectedly passed closure validation.' }

    Copy-Item -LiteralPath (Join-Path $ReleaseOutput 'dependency-inventory.json') -Destination $inventoryPath -Force
    $layout = Get-Content -Raw -LiteralPath $layoutPath | ConvertFrom-Json
    $serverPackage = $layout.packages | Where-Object packageId -eq 'anomaly-server'
    ($serverPackage.entries | Where-Object path -eq 'dependencies/global/0Harmony.dll').assemblyVersion = '2.16.0.0'
    [IO.File]::WriteAllBytes(
        $layoutPath,
        [Text.UTF8Encoding]::new($false).GetBytes(($layout | ConvertTo-Json -Depth 16)))

    & dotnet run --project (Join-Path $closureRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release --no-build -- audit-closure $closureTempDirectory
    if ($LASTEXITCODE -eq 0) { throw 'Mismatched packaged server Harmony version unexpectedly passed closure validation.' }

    Copy-Item -LiteralPath (Join-Path $ReleaseOutput 'package-layout.json') -Destination $layoutPath -Force
    $layout = Get-Content -Raw -LiteralPath $layoutPath | ConvertFrom-Json
    $serverPackage = $layout.packages | Where-Object packageId -eq 'anomaly-server'
    ($serverPackage.runtimeClosure.hostProvidedAssemblies |
        Where-Object name -eq 'BouncyCastle.Cryptography').assemblyVersion = '3.0.0.0'
    [IO.File]::WriteAllBytes(
        $layoutPath,
        [Text.UTF8Encoding]::new($false).GetBytes(($layout | ConvertTo-Json -Depth 16)))

    & dotnet run --project (Join-Path $closureRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release --no-build -- audit-closure $closureTempDirectory
    if ($LASTEXITCODE -eq 0) { throw 'Mismatched server Bouncy Castle host version unexpectedly passed closure validation.' }

    Copy-Item -LiteralPath (Join-Path $ReleaseOutput 'package-layout.json') -Destination $layoutPath -Force
    $inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
    $resolvedNewtonsoft = @($inventory.resolvedInputs | Where-Object {
        $_.project -eq 'Anomaly.Client' -and $_.name -eq 'Newtonsoft.Json.dll'
    })
    if ($resolvedNewtonsoft.Count -ne 1) { throw 'Expected one captured Anomaly.Client Newtonsoft input.' }
    $resolvedNewtonsoft[0].fileVersion = '13.0.5.0'
    [IO.File]::WriteAllBytes(
        $inventoryPath,
        [Text.UTF8Encoding]::new($false).GetBytes(($inventory | ConvertTo-Json -Depth 16)))

    & dotnet run --project (Join-Path $closureRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release --no-build -- audit-closure $closureTempDirectory
    if ($LASTEXITCODE -eq 0) { throw 'Mismatched resolved Newtonsoft file version unexpectedly passed closure validation.' }

    Copy-Item -LiteralPath (Join-Path $ReleaseOutput 'dependency-inventory.json') -Destination $inventoryPath -Force
    $inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
    $bundleModule = @($inventory.resolvedInputs | Where-Object assemblyName -eq 'UnityEngine.AssetBundleModule')
    if ($bundleModule.Count -eq 0) { throw 'Expected captured Unity AssetBundle module reference.' }
    foreach ($inputAssembly in $bundleModule) { $inputAssembly.assemblyName = 'Unexpected.BundleModule' }
    [IO.File]::WriteAllBytes(
        $inventoryPath,
        [Text.UTF8Encoding]::new($false).GetBytes(($inventory | ConvertTo-Json -Depth 16)))
    & dotnet run --project (Join-Path $closureRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release --no-build -- audit-closure $closureTempDirectory
    if ($LASTEXITCODE -eq 0) { throw 'A matching filename with the wrong assembly identity unexpectedly passed closure validation.' }

    $global:LASTEXITCODE = 0
    Write-Host 'Positive, missing-dependency, assembly-identity, Harmony, Bouncy Castle, and Newtonsoft file-version runtime closure gates passed.'
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
