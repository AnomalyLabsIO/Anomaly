#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [string] $ReleaseOutput = ''
)

$ErrorActionPreference = 'Stop'
$runtimeTestRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($ReleaseOutput)) {
    $ReleaseOutput = Join-Path $runtimeTestRepository ("artifacts/runtime-{0}" -f $Version)
}
$ReleaseOutput = [IO.Path]::GetFullPath($ReleaseOutput)
$runtimeTestPackager = Join-Path $runtimeTestRepository 'tools/ReleasePackaging/bin/Release/net8.0/Anomaly.ReleasePackaging.dll'
$runtimeTestSkinDirectory = Join-Path $PSScriptRoot 'assets/skins'
$runtimeTestTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$runtimeTestTemp = Join-Path $runtimeTestTempRoot ("anomaly-runtime-test-{0}" -f [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $runtimeTestTemp)
try {
    $expectedDlls = @{
        'anomaly-client' = @(
            'Plugins/Anomaly.Bootstrap.dll', 'Mods/Anomaly.Client.dll',
            'UserLibs/Anomaly.Client.Api.dll', 'UserLibs/Anomaly.Shared.dll',
            'UserLibs/Anomaly.AssetValidation.dll', 'UserLibs/BouncyCastle.Cryptography.dll',
            'UserLibs/AssetsTools.NET.dll', 'UserLibs/YamlDotNet.dll'
        )
        'anomaly-server' = @(
            'plugins/global/Anomaly.Server.dll', 'dependencies/global/Anomaly.Shared.dll',
            'dependencies/global/0Harmony.dll'
        )
    }
    $artifacts = @(Get-ChildItem -LiteralPath $ReleaseOutput -Force)
    $expectedArtifacts = @(
        "anomaly-client-$Version.zip", "anomaly-server-$Version.zip", "anomaly-sdk-$Version.zip", 'SHA256SUMS'
    )
    if (@(Compare-Object ($expectedArtifacts | Sort-Object) ($artifacts.Name | Sort-Object) -CaseSensitive).Count -ne 0) {
        throw 'Local release output must contain exactly the client, server, SDK ZIPs and SHA256SUMS.'
    }
    $archives = @($artifacts | Where-Object Extension -eq '.zip')
    $expectedChecksums = @($archives | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant(),$_.Name
    })
    $checksumLines = @(Get-Content -LiteralPath (Join-Path $ReleaseOutput 'SHA256SUMS'))
    if (@(Compare-Object $expectedChecksums $checksumLines -CaseSensitive).Count -ne 0) {
        throw 'SHA256SUMS does not match all three archives.'
    }
    foreach ($packageId in @('anomaly-client','anomaly-server')) {
        $archivePath = Join-Path $ReleaseOutput ("{0}-{1}.zip" -f $packageId,$Version)
        $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            $paths = @($archive.Entries.FullName)
            $expectedPaths = @($expectedDlls[$packageId])
            if ($packageId -eq 'anomaly-client') {
                $expectedPaths += @(
                    'UserData/Anomaly/AssetBundles/personal-skins.bundle',
                    'UserData/Anomaly/AssetBundles/personal-skins.index.json'
                )
            }
            $difference = @(Compare-Object ($expectedPaths | Sort-Object) ($paths | Sort-Object) -CaseSensitive)
            if ($difference.Count -ne 0) { throw "Missing or unexpected runtime archive files in $packageId." }
            $userData = @($paths | Where-Object { $_ -like 'UserData/*' })
            if ($packageId -eq 'anomaly-client') {
                $skinFiles = @('personal-skins.bundle','personal-skins.index.json')
                if ($userData.Count -ne 2) { throw 'Client must carry exactly the two skin pack files in UserData.' }
                foreach ($name in $skinFiles) {
                    $entry = $archive.GetEntry("UserData/Anomaly/AssetBundles/$name")
                    if ($null -eq $entry) { throw "Client is missing the skin file $name." }
                    $stream = $entry.Open()
                    try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
                    finally { $stream.Dispose() }
                    if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $runtimeTestSkinDirectory $name)).Hash) {
                        throw "Archived skin bytes differ from the checked-in pack: $name"
                    }
                }
            } elseif ($userData.Count -ne 0) { throw 'Server must not carry client skins or profiles.' }
        }
        finally { $archive.Dispose() }
    }

    $sdkDefinition = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'package-layout.json') |
        ConvertFrom-Json).packages | Where-Object packageId -eq 'anomaly-sdk'
    $sdkArchive = [IO.Compression.ZipFile]::OpenRead((Join-Path $ReleaseOutput "anomaly-sdk-$Version.zip"))
    try {
        $difference = @(Compare-Object ($sdkDefinition.entries.path | Sort-Object) `
            ($sdkArchive.Entries.FullName | Sort-Object) -CaseSensitive)
        if ($difference.Count -ne 0) { throw 'Missing or unexpected SDK files.' }
        foreach ($definition in $sdkDefinition.entries) {
            $entry = $sdkArchive.GetEntry($definition.path)
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $runtimeTestRepository $definition.source)).Hash) {
                throw "SDK file differs from the declared build/source input: $($definition.path)"
            }
        }
    }
    finally { $sdkArchive.Dispose() }

    $beforeHashes = @($artifacts | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash })
    $failure = @(& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Build-RuntimeRelease.ps1') `
        -Version $Version -OutputDirectory $ReleaseOutput 2>&1)
    if ($LASTEXITCODE -eq 0 -or ($failure -join "`n") -notlike '*Use an absent or empty output directory*') {
        throw 'Reusing an output directory was not rejected before building.'
    }
    $afterHashes = @($artifacts | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash })
    if (@(Compare-Object $beforeHashes $afterHashes).Count -ne 0) { throw 'Existing archives were altered.' }

    foreach ($name in @('personal-skins.bundle','personal-skins.index.json')) {
        Copy-Item -LiteralPath (Join-Path $runtimeTestSkinDirectory $name) -Destination $runtimeTestTemp
    }
    $testIndex = Join-Path $runtimeTestTemp 'personal-skins.index.json'
    $testBundle = Join-Path $runtimeTestTemp 'personal-skins.bundle'
    & dotnet $runtimeTestPackager validate-skins $testIndex
    if ($LASTEXITCODE -ne 0) { throw 'Valid default skin pack was rejected.' }

    $bundle = [IO.File]::Open($testBundle, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
    try { $first = $bundle.ReadByte(); $bundle.Position = 0; $bundle.WriteByte($first -bxor 1) }
    finally { $bundle.Dispose() }
    $failure = @(& dotnet $runtimeTestPackager validate-skins $testIndex 2>&1)
    if ($LASTEXITCODE -eq 0 -or ($failure -join "`n") -notlike '*Bundle SHA-256 mismatch*') {
        throw 'Corrupt skin bundle was not rejected by its index hash.'
    }

    Copy-Item -LiteralPath (Join-Path $runtimeTestSkinDirectory 'personal-skins.bundle') -Destination $testBundle -Force
    $index = Get-Content -Raw -LiteralPath $testIndex | ConvertFrom-Json
    $index.Assets = @($index.Assets | Where-Object Address -ne 'game/skin/background')
    [IO.File]::WriteAllText($testIndex, ($index | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $failure = @(& dotnet $runtimeTestPackager validate-skins $testIndex 2>&1)
    if ($LASTEXITCODE -eq 0 -or ($failure -join "`n") -notlike '*exactly the logo and background*') {
        throw 'Incomplete logo/background pack was not rejected.'
    }
    Write-Host 'Runtime/SDK ZIP layouts, SDK file hashes, SHA256SUMS, skin rejection, and overwrite protection passed.'
}
finally {
    $runtimeResolvedTestTemp = [IO.Path]::GetFullPath($runtimeTestTemp)
    $runtimeTestTempPrefix = $runtimeTestTempRoot.TrimEnd([char[]]@('\','/')) + [IO.Path]::DirectorySeparatorChar
    if ($runtimeResolvedTestTemp.StartsWith($runtimeTestTempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $runtimeResolvedTestTemp).StartsWith('anomaly-runtime-test-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $runtimeResolvedTestTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
