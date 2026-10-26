[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,
    [string] $OutputDirectory = '',
    [string] $SourceCommit = '',
    [long] $SourceDateEpoch = -1,
    [string] $SigningKeyPath = '',
    [string] $PublicKeyPath = '',
    [string] $KeyId = '',
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$releaseRepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $releaseRepositoryRoot ("artifacts/release-{0}" -f $Version)
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

$currentSourceCommit = (& git -C $releaseRepositoryRoot rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the current source commit.' }
if ([string]::IsNullOrWhiteSpace($SourceCommit)) {
    $SourceCommit = $currentSourceCommit
} elseif ($SourceCommit.ToLowerInvariant() -ne $currentSourceCommit) {
    throw 'SourceCommit must identify the currently checked-out commit.'
}
$sourceStatus = @(& git -C $releaseRepositoryRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Unable to verify the release source worktree.' }
if ($sourceStatus.Count -ne 0) {
    throw 'Release packaging requires a clean worktree so every emitted byte is attributable to SourceCommit.'
}
if ($SourceDateEpoch -lt 0) {
    $epochText = (& git -C $releaseRepositoryRoot show -s --format=%ct $SourceCommit).Trim()
    if ($LASTEXITCODE -ne 0 -or -not [long]::TryParse($epochText, [ref]$SourceDateEpoch)) {
        throw 'Unable to resolve SOURCE_DATE_EPOCH from the source commit.'
    }
}

$releaseReferenceDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("anomaly-release-{0}" -f [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $releaseReferenceDirectory)
$previousSourceDateEpoch = $env:SOURCE_DATE_EPOCH
try {
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $releaseRepositoryRoot 'Anomaly.slnx') `
            -c Release -p:DeployLocal=false -p:ContinuousIntegrationBuild=true `
            -warnaserror --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Release/x64 solution build failed.' }
    }

    $runtimeProjects = @(
        @{ Name = 'Anomaly.Bootstrap'; Path = 'Anomaly.Bootstrap/Anomaly.Bootstrap.csproj' },
        @{ Name = 'Anomaly.Client'; Path = 'Anomaly.Client/Anomaly.Client.csproj' },
        @{ Name = 'Anomaly.Client.Api'; Path = 'Anomaly.Client.Api/Anomaly.Client.Api.csproj' },
        @{ Name = 'Anomaly.Client.Scripting'; Path = 'Anomaly.Client.Scripting/Anomaly.Client.Scripting.csproj' },
        @{ Name = 'Anomaly.AssetValidation'; Path = 'Anomaly.AssetValidation/Anomaly.AssetValidation.csproj' },
        @{ Name = 'Anomaly.Server'; Path = 'Anomaly.Server/Anomaly.Server.csproj' },
        @{ Name = 'Anomaly.Shared'; Path = 'Anomaly.Shared/Anomaly.Shared.csproj' }
    )
    foreach ($runtimeProject in $runtimeProjects) {
        $referenceList = Join-Path $releaseReferenceDirectory ($runtimeProject.Name + '.refs')
        & dotnet msbuild (Join-Path $releaseRepositoryRoot $runtimeProject.Path) `
            -t:WriteAnomalyResolvedReferences -p:Configuration=Release `
            -p:DeployLocal=false -p:ContinuousIntegrationBuild=true `
            -p:BuildProjectReferences=false `
            "-p:AnomalyResolvedReferenceList=$referenceList" -nologo
        if ($LASTEXITCODE -ne 0) {
            throw ("Failed to capture resolved references for {0}." -f $runtimeProject.Name)
        }
    }

    & dotnet run --project (Join-Path $releaseRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release -- create $releaseRepositoryRoot $OutputDirectory $Version `
        $SourceCommit $SourceDateEpoch $releaseReferenceDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Release package creation failed.' }

    & dotnet run --project (Join-Path $releaseRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release --no-build -- verify $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Post-package verification failed.' }

    $hasSigningInputs = -not [string]::IsNullOrWhiteSpace($SigningKeyPath) -or
        -not [string]::IsNullOrWhiteSpace($PublicKeyPath) -or
        -not [string]::IsNullOrWhiteSpace($KeyId)
    if ($hasSigningInputs) {
        if ([string]::IsNullOrWhiteSpace($SigningKeyPath) -or
            [string]::IsNullOrWhiteSpace($PublicKeyPath) -or
            [string]::IsNullOrWhiteSpace($KeyId)) {
            throw 'SigningKeyPath, PublicKeyPath, and KeyId are all required for a signed release.'
        }
        $SigningKeyPath = [System.IO.Path]::GetFullPath($SigningKeyPath)
        $PublicKeyPath = [System.IO.Path]::GetFullPath($PublicKeyPath)
        if (-not (Test-Path -LiteralPath $SigningKeyPath -PathType Leaf)) { throw 'Signing key file is missing.' }
        if (-not (Test-Path -LiteralPath $PublicKeyPath -PathType Leaf)) { throw 'Public verification key file is missing.' }

        $env:SOURCE_DATE_EPOCH = $SourceDateEpoch.ToString([Globalization.CultureInfo]::InvariantCulture)
        & dotnet build (Join-Path $releaseRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
            -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Release manifest tool build failed.' }

        $inventoryPath = Join-Path $OutputDirectory 'dependency-inventory.json'
        $inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
        $manifestDirectory = Join-Path $OutputDirectory 'manifests'
        [void](New-Item -ItemType Directory -Path $manifestDirectory)
        foreach ($package in $inventory.packages) {
            $manifestPath = Join-Path $manifestDirectory ($package.packageId + '.json')
            $manifestArguments = @(
                'run', '--project', (Join-Path $releaseRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj'),
                '-c', 'Release', '--no-build', '--', 'create', $KeyId, $package.target,
                $package.packageId, $package.packageId, $Version, $SourceCommit,
                $inventoryPath, $manifestPath, $SigningKeyPath
            )
            foreach ($entry in $package.entries) {
                $manifestArguments += Join-Path (Join-Path $OutputDirectory ('payloads/' + $package.target)) $entry.path
            }
            & dotnet @manifestArguments
            if ($LASTEXITCODE -ne 0) { throw ("Signing {0} failed." -f $package.packageId) }

            & dotnet run --project (Join-Path $releaseRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
                -c Release --no-build -- verify $manifestPath ($manifestPath + '.sig') `
                $PublicKeyPath (Join-Path $OutputDirectory ('payloads/' + $package.target)) $inventoryPath
            if ($LASTEXITCODE -ne 0) { throw ("Signature verification failed for {0}." -f $package.packageId) }
        }

        $clientManifestPath = Join-Path $manifestDirectory 'anomaly-client.json'
        $officialEnvelope = [ordered]@{
            manifestBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($clientManifestPath))
            signatureBase64 = [IO.File]::ReadAllText($clientManifestPath + '.sig').Trim()
            dependencyInventoryBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($inventoryPath))
        }
        $envelopeBytes = [Text.UTF8Encoding]::new($false).GetBytes(($officialEnvelope | ConvertTo-Json -Depth 4))
        [IO.File]::WriteAllBytes((Join-Path $manifestDirectory 'anomaly-client-official-envelope.json'), $envelopeBytes)

        # Record only public signing evidence. The receipt retains the verified
        # keyId and hashes of the signed manifests/signatures/envelope; private
        # and public key paths or bytes are never copied into release metadata.
        & dotnet run --project (Join-Path $releaseRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
            -c Release --no-build -- record-signing $OutputDirectory $KeyId $PublicKeyPath
        if ($LASTEXITCODE -ne 0) { throw 'Recording release signing evidence failed.' }
    }

    Write-Host ("Release output verified at {0}" -f $OutputDirectory)
}
finally {
    $env:SOURCE_DATE_EPOCH = $previousSourceDateEpoch
    $resolvedReferenceDirectory = [System.IO.Path]::GetFullPath($releaseReferenceDirectory)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedReferenceDirectory.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedReferenceDirectory).StartsWith('anomaly-release-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedReferenceDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
