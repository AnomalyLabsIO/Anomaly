[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$toolRepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolTempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("anomaly-release-signing-test-{0}" -f [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $toolTempDirectory)
$previousSourceDateEpoch = $env:SOURCE_DATE_EPOCH
try {
    $privateKey = Join-Path $toolTempDirectory 'test-private.pem'
    $publicKey = Join-Path $toolTempDirectory 'test-public.pem'
    $otherPrivateKey = Join-Path $toolTempDirectory 'other-private.pem'
    $otherPublicKey = Join-Path $toolTempDirectory 'other-public.pem'
    $inventory = Join-Path $toolTempDirectory 'dependency-inventory.json'
    [IO.File]::WriteAllBytes($inventory, [Text.UTF8Encoding]::new($false).GetBytes('{"schemaVersion":1}'))

    $artifactNames = @(
        'Anomaly.Bootstrap.dll',
        'Anomaly.Client.Api.dll',
        'Anomaly.Client.dll',
        'Anomaly.Shared.dll'
    )
    foreach ($artifactName in $artifactNames) {
        [IO.File]::WriteAllBytes(
            (Join-Path $toolTempDirectory $artifactName),
            [Text.UTF8Encoding]::new($false).GetBytes(('test:' + $artifactName)))
    }

    & dotnet build (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release -p:DeployLocal=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release manifest tool build failed.' }
    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- generate-key $privateKey $publicKey
    if ($LASTEXITCODE -ne 0) { throw 'Test key generation failed.' }
    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- generate-key $otherPrivateKey $otherPublicKey
    if ($LASTEXITCODE -ne 0) { throw 'Second test key generation failed.' }

    $env:SOURCE_DATE_EPOCH = '1787356800'
    $manifest = Join-Path $toolTempDirectory 'release.json'
    $createArguments = @(
        'run', '--project', (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj'),
        '-c', 'Release', '--no-build', '--', 'create', 'test-rotation-a', 'client-runtime',
        'anomaly-client', 'anomaly-client', '1.0.0', ('a' * 40), $inventory, $manifest,
        $privateKey
    )
    foreach ($artifactName in $artifactNames) { $createArguments += Join-Path $toolTempDirectory $artifactName }
    & dotnet @createArguments
    if ($LASTEXITCODE -ne 0) { throw 'Schema-v2 manifest creation failed.' }

    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- verify $manifest ($manifest + '.sig') $publicKey `
        $toolTempDirectory $inventory
    if ($LASTEXITCODE -ne 0) { throw 'Schema-v2 manifest verification failed.' }

    $manifestObject = Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
    $expectedFields = @(
        'schemaVersion', 'keyId', 'target', 'packageId', 'releaseId', 'version',
        'sourceCommit', 'dependencyInventorySha256', 'generatedUtc', 'artifacts'
    )
    $actualFields = @($manifestObject.PSObject.Properties.Name)
    if (($actualFields -join ',') -ne ($expectedFields -join ',')) {
        throw ("Manifest fields do not match schema v2: {0}" -f ($actualFields -join ', '))
    }

    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- verify $manifest ($manifest + '.sig') $otherPublicKey
    if ($LASTEXITCODE -eq 0) { throw 'Wrong-key verification unexpectedly succeeded.' }

    $missingArtifact = Join-Path $toolTempDirectory 'Anomaly.Shared.dll'
    $missingArtifactBackup = $missingArtifact + '.missing'
    [IO.File]::Move($missingArtifact, $missingArtifactBackup)
    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- verify $manifest ($manifest + '.sig') $publicKey `
        $toolTempDirectory $inventory
    if ($LASTEXITCODE -eq 0) { throw 'Missing artifact verification unexpectedly succeeded.' }
    [IO.File]::Move($missingArtifactBackup, $missingArtifact)

    [IO.File]::AppendAllText((Join-Path $toolTempDirectory 'Anomaly.Client.dll'), 'tampered')
    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- verify $manifest ($manifest + '.sig') $publicKey `
        $toolTempDirectory $inventory
    if ($LASTEXITCODE -eq 0) { throw 'Tampered artifact verification unexpectedly succeeded.' }

    $global:LASTEXITCODE = 0
    Write-Host 'Optional release manifest signing and tamper gates passed.'
}
finally {
    $env:SOURCE_DATE_EPOCH = $previousSourceDateEpoch
    $resolvedToolTemp = [System.IO.Path]::GetFullPath($toolTempDirectory)
    $resolvedSystemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedToolTemp.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedToolTemp).StartsWith('anomaly-release-signing-test-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedToolTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
