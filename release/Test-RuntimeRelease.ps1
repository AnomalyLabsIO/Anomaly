#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [string] $ReleaseOutput = '',
    [string] $SkinIndexPath = ''
)

$ErrorActionPreference = 'Stop'
$runtimeTestRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($ReleaseOutput)) {
    $ReleaseOutput = Join-Path $runtimeTestRepository ("artifacts/runtime-{0}" -f $Version)
}
$ReleaseOutput = [IO.Path]::GetFullPath($ReleaseOutput)
$runtimeTestPackager = Join-Path $runtimeTestRepository 'tools/ReleasePackaging/bin/Release/net8.0/Anomaly.ReleasePackaging.dll'
if ([string]::IsNullOrWhiteSpace($SkinIndexPath)) {
    $SkinIndexPath = Join-Path $PSScriptRoot 'assets/skins/personal-skins.index.json'
}
$runtimeTestSkinIndex = [IO.Path]::GetFullPath($SkinIndexPath)
$runtimeTestSkin = Get-Content -Raw -LiteralPath $runtimeTestSkinIndex | ConvertFrom-Json
$runtimeTestSkinBundle = Join-Path (Split-Path $runtimeTestSkinIndex) $runtimeTestSkin.BundleFile
$runtimeTestSkinRoot = 'UserData/Anomaly/ContentPacks/' + $runtimeTestSkin.PackId.Replace(':','/')
$runtimeTestSkinIndexRelative = 'assets/pack.index.json'
$runtimeTestTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$runtimeTestTemp = Join-Path $runtimeTestTempRoot ("anomaly-runtime-test-{0}" -f [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $runtimeTestTemp)
try {
    $expectedDlls = @{
        'anomaly-client' = @(
            'Plugins/Anomaly.Bootstrap.dll', 'Mods/Anomaly.Client.dll',
            'UserLibs/Anomaly.Client.Api.dll', 'UserLibs/Anomaly.Shared.dll',
            'UserLibs/Anomaly.Client.Scripting.dll', 'UserLibs/Anomaly.MoonSharp.Interpreter.dll',
            'UserLibs/Anomaly.AssetValidation.dll', 'UserLibs/BouncyCastle.Cryptography.dll',
            'UserLibs/AssetsTools.NET.dll', 'UserLibs/YamlDotNet.dll'
        )
        'anomaly-server' = @(
            'plugins/global/Anomaly.Server.dll', 'dependencies/global/Anomaly.Shared.dll',
            'dependencies/global/0Harmony.dll'
        )
    }
    $packageLayout = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'package-layout.json') | ConvertFrom-Json
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
            $definition = @($packageLayout.packages | Where-Object packageId -eq $packageId)
            if ($definition.Count -ne 1) { throw "Expected one package definition for $packageId." }
            # Notice/provenance documents are runtime payloads too. The complete
            # manifest controls the exact layout; DLLs retain an independent list
            # so dropping a required dependency from the manifest still fails.
            $expectedPaths = @($definition[0].entries.path)
            if ($packageId -eq 'anomaly-client') {
                $expectedPaths = @($expectedPaths | Where-Object { !$_.StartsWith('UserData/Anomaly/ContentPacks/', [StringComparison]::Ordinal) }) +
                    @("$runtimeTestSkinRoot/pack.json", "$runtimeTestSkinRoot/$runtimeTestSkinIndexRelative", "$runtimeTestSkinRoot/assets/$($runtimeTestSkin.BundleFile)")
            }
            $difference = @(Compare-Object ($expectedPaths | Sort-Object) ($paths | Sort-Object) -CaseSensitive)
            if ($difference.Count -ne 0) { throw "Missing or unexpected runtime archive files in $packageId." }
            $archivedDlls = @($paths | Where-Object { $_.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) })
            $dllDifference = @(Compare-Object ($expectedDlls[$packageId] | Sort-Object) ($archivedDlls | Sort-Object) -CaseSensitive)
            if ($dllDifference.Count -ne 0) { throw "Missing or unexpected runtime DLLs in $packageId." }
            foreach ($file in $definition[0].entries) {
                if ($packageId -eq 'anomaly-client' -and $file.path.StartsWith('UserData/Anomaly/ContentPacks/', [StringComparison]::Ordinal)) { continue }
                $entry = $archive.GetEntry($file.path)
                $stream = $entry.Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
                finally { $stream.Dispose() }
                if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $runtimeTestRepository $file.source)).Hash) {
                    throw "Runtime file differs from the declared build/source input: $($file.path)"
                }
            }
            $userData = @($paths | Where-Object { $_ -like 'UserData/*' })
            if ($packageId -eq 'anomaly-client') {
                if ($userData.Count -ne 3) { throw 'Client must carry exactly the descriptor and its two declared skin assets in UserData.' }
                $descriptorEntry = $archive.GetEntry("$runtimeTestSkinRoot/pack.json")
                if ($descriptorEntry.Length -gt 16384) { throw 'Skin descriptor is oversized.' }
                $descriptorReader = [IO.StreamReader]::new($descriptorEntry.Open())
                try { $descriptor = $descriptorReader.ReadToEnd() | ConvertFrom-Json }
                finally { $descriptorReader.Dispose() }
                if ($descriptor.SchemaVersion -ne 1 -or
                    $descriptor.PackId -cne $runtimeTestSkin.PackId -or $descriptor.Assets.PackId -cne $runtimeTestSkin.PackId -or
                    $descriptor.Assets.Index -cne $runtimeTestSkinIndexRelative) {
                    throw 'Skin descriptor does not explicitly declare the selected asset index and identity.'
                }
                foreach ($skinFile in @(
                    @{path="$runtimeTestSkinRoot/$runtimeTestSkinIndexRelative"; source=$runtimeTestSkinIndex},
                    @{path="$runtimeTestSkinRoot/assets/$($runtimeTestSkin.BundleFile)"; source=$runtimeTestSkinBundle}
                )) {
                    $entry = $archive.GetEntry($skinFile.path)
                    if ($null -eq $entry) { throw "Client is missing the declared skin asset $($skinFile.path)." }
                    $stream = $entry.Open()
                    try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
                    finally { $stream.Dispose() }
                    if ($hash -ne (Get-FileHash -LiteralPath $skinFile.source).Hash) {
                        throw "Archived skin bytes differ from the selected pack: $($skinFile.path)"
                    }
                }
            } elseif ($userData.Count -ne 0) { throw 'Server must not carry client skins or profiles.' }
        }
        finally { $archive.Dispose() }
    }

    $sdkDefinition = $packageLayout.packages | Where-Object packageId -eq 'anomaly-sdk'
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

    $testIndex = Join-Path $runtimeTestTemp ([IO.Path]::GetFileName($runtimeTestSkinIndex))
    $testBundle = Join-Path $runtimeTestTemp $runtimeTestSkin.BundleFile
    Copy-Item -LiteralPath $runtimeTestSkinIndex -Destination $testIndex
    Copy-Item -LiteralPath $runtimeTestSkinBundle -Destination $testBundle
    & dotnet $runtimeTestPackager validate-skins $testIndex
    if ($LASTEXITCODE -ne 0) { throw 'Valid default skin pack was rejected.' }

    $bundle = [IO.File]::Open($testBundle, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
    try { $first = $bundle.ReadByte(); $bundle.Position = 0; $bundle.WriteByte($first -bxor 1) }
    finally { $bundle.Dispose() }
    $failure = @(& dotnet $runtimeTestPackager validate-skins $testIndex 2>&1)
    if ($LASTEXITCODE -eq 0 -or ($failure -join "`n") -notlike '*Bundle SHA-256 mismatch*') {
        throw 'Corrupt skin bundle was not rejected by its index hash.'
    }

    Copy-Item -LiteralPath $runtimeTestSkinBundle -Destination $testBundle -Force
    $index = Get-Content -Raw -LiteralPath $testIndex | ConvertFrom-Json
    $index.Assets = @($index.Assets | Where-Object Address -ne 'game/skin/background')
    [IO.File]::WriteAllText($testIndex, ($index | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $failure = @(& dotnet $runtimeTestPackager validate-skins $testIndex 2>&1)
    if ($LASTEXITCODE -eq 0 -or ($failure -join "`n") -notlike '*exactly the logo and background*') {
        throw 'Incomplete logo/background pack was not rejected.'
    }
    Write-Host 'Runtime/SDK ZIP layouts, runtime/SDK file hashes, SHA256SUMS, skin rejection, and overwrite protection passed.'
}
finally {
    $runtimeResolvedTestTemp = [IO.Path]::GetFullPath($runtimeTestTemp)
    $runtimeTestTempPrefix = $runtimeTestTempRoot.TrimEnd([char[]]@('\','/')) + [IO.Path]::DirectorySeparatorChar
    if ($runtimeResolvedTestTemp.StartsWith($runtimeTestTempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $runtimeResolvedTestTemp).StartsWith('anomaly-runtime-test-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $runtimeResolvedTestTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
