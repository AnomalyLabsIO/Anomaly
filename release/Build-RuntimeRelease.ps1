#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$')]
    [string] $Version,
    [string] $OutputDirectory = '',
    [string] $SkinIndexPath = '',
    [string] $ClientRoot = '',
    [string] $ServerGameDirectory = '',
    [string] $ServerReferenceDirectory = '',
    [string] $LabApiDirectory = ''
)

$ErrorActionPreference = 'Stop'
$runtimeRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runtimeRepositoryRoot ("artifacts/runtime-{0}" -f $Version)
}
if ([string]::IsNullOrWhiteSpace($SkinIndexPath)) {
    $SkinIndexPath = Join-Path $runtimeRepositoryRoot 'release/assets/skins/personal-skins.index.json'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$SkinIndexPath = [IO.Path]::GetFullPath($SkinIndexPath)
if (Test-Path -LiteralPath $OutputDirectory) {
    if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container) -or
        @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) {
        throw "Use an absent or empty output directory: $OutputDirectory"
    }
}
if (-not (Test-Path -LiteralPath $SkinIndexPath -PathType Leaf)) {
    throw "Skin index is missing: $SkinIndexPath. Supply -SkinIndexPath with an exported logo/background *.index.json and its adjacent .bundle."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK (8 or later) must be on PATH.'
}

$runtimeBuildProperties = @('-p:DeployLocal=false')
foreach ($override in @(
    @{ Property = 'AnomalyClientRoot'; Value = $ClientRoot },
    @{ Property = 'AnomalyServerGameDir'; Value = $ServerGameDirectory },
    @{ Property = 'AnomalyServerReferenceDir'; Value = $ServerReferenceDirectory },
    @{ Property = 'AnomalyLabApiDir'; Value = $LabApiDirectory }
)) {
    if (-not [string]::IsNullOrWhiteSpace($override.Value)) {
        $runtimeBuildProperties += '-p:{0}={1}' -f $override.Property,[IO.Path]::GetFullPath($override.Value)
    }
}

$runtimeProjects = @(
    'Anomaly.Bootstrap', 'Anomaly.Client', 'Anomaly.Client.Api', 'Anomaly.Client.Scripting',
    'Anomaly.AssetValidation', 'Anomaly.Server', 'Anomaly.Shared'
)
$packagerProject = Join-Path $runtimeRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj'
$packagerAssembly = Join-Path $runtimeRepositoryRoot 'tools/ReleasePackaging/bin/Release/net8.0/Anomaly.ReleasePackaging.dll'
$runtimeScratchRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$runtimeScratchDirectory = Join-Path $runtimeScratchRoot ("anomaly-runtime-release-{0}" -f [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $runtimeScratchDirectory)
try {
    & (Join-Path $PSScriptRoot 'Verify-CompileLists.ps1')
    & dotnet build $packagerProject -c Release @runtimeBuildProperties --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Runtime packager build failed.' }
    & dotnet $packagerAssembly validate-skins $SkinIndexPath
    if ($LASTEXITCODE -ne 0) { throw 'Skin pack validation failed.' }

    # Client and Server build Api, Scripting (including the private MoonSharp
    # fork), Shared, and AssetValidation transitively.
    foreach ($project in @('Anomaly.Bootstrap', 'Anomaly.Client', 'Anomaly.Server')) {
        & dotnet build (Join-Path $runtimeRepositoryRoot "$project/$project.csproj") `
            -c Release @runtimeBuildProperties --nologo -v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Release build failed for $project." }
    }
    foreach ($project in $runtimeProjects) {
        $referenceList = Join-Path $runtimeScratchDirectory ($project + '.refs')
        & dotnet msbuild (Join-Path $runtimeRepositoryRoot "$project/$project.csproj") `
            -t:WriteAnomalyResolvedReferences -p:Configuration=Release `
            @runtimeBuildProperties -p:BuildProjectReferences=false `
            "-p:AnomalyResolvedReferenceList=$referenceList" -nologo -v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Reference capture failed for $project." }
    }
    # Each ZIP carries its reviewed payload and redistribution notices.
    $localReleaseStage = Join-Path $runtimeScratchDirectory 'release-output'
    & dotnet $packagerAssembly create-local $runtimeRepositoryRoot $localReleaseStage `
        $Version $runtimeScratchDirectory $SkinIndexPath
    if ($LASTEXITCODE -ne 0) { throw 'Release archive creation or dependency verification failed.' }

    $clientReferenceJson = @(& dotnet msbuild `
        (Join-Path $runtimeRepositoryRoot 'Anomaly.Client.Api/Anomaly.Client.Api.csproj') `
        @runtimeBuildProperties -getProperty:AnomalyClientInteropDir,AnomalyClientMelonLibDir -nologo)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve client SDK references.' }
    $clientSdkReferences = ($clientReferenceJson -join [Environment]::NewLine | ConvertFrom-Json).Properties
    $serverReferenceJson = @(& dotnet msbuild `
        (Join-Path $runtimeRepositoryRoot 'Anomaly.Server/Anomaly.Server.csproj') `
        @runtimeBuildProperties -getProperty:AnomalyServerReferenceDir,AnomalyLabApiDir -nologo)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve server SDK references.' }
    $serverSdkReferences = ($serverReferenceJson -join [Environment]::NewLine | ConvertFrom-Json).Properties
    & (Join-Path $PSScriptRoot 'Test-SdkCompileClosure.ps1') -ReleaseOutput $runtimeScratchDirectory `
        -AnomalyClientInteropDir $clientSdkReferences.AnomalyClientInteropDir `
        -AnomalyClientMelonLibDir $clientSdkReferences.AnomalyClientMelonLibDir `
        -AnomalyServerReferenceDir $serverSdkReferences.AnomalyServerReferenceDir `
        -AnomalyLabApiDir $serverSdkReferences.AnomalyLabApiDir
    if ($LASTEXITCODE -ne 0) { throw 'Packaged SDK compile verification failed.' }

    if ((Test-Path -LiteralPath $OutputDirectory) -and
        (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container) -or
         @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0)) {
        throw "Use an absent or empty output directory: $OutputDirectory"
    }
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    foreach ($artifact in Get-ChildItem -LiteralPath $localReleaseStage -File) {
        [IO.File]::Copy($artifact.FullName, (Join-Path $OutputDirectory $artifact.Name), $false)
    }
    Write-Host "Client, server, SDK ZIPs and SHA256SUMS are ready in $OutputDirectory"
}
finally {
    $runtimeResolvedScratch = [IO.Path]::GetFullPath($runtimeScratchDirectory)
    $runtimeTempPrefix = $runtimeScratchRoot.TrimEnd([char[]]@('\','/')) + [IO.Path]::DirectorySeparatorChar
    if ($runtimeResolvedScratch.StartsWith($runtimeTempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $runtimeResolvedScratch).StartsWith('anomaly-runtime-release-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $runtimeResolvedScratch -Recurse -Force -ErrorAction SilentlyContinue
    }
}
