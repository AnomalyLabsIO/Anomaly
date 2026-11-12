[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [string] $AuthoringProject = 'E:/Projects/AnomalyLabs/Anomaly.AssetAuthoring',
    [string] $UnityEditor = 'C:/Program Files/6000.0.43f1/Editor/Unity.exe',
    [string] $BasePackIndexPath = '',
    [ValidateRange(1,8)] [int] $PackCount = 8,
    [switch] $SkipBuild
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$artifacts = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (!$output.StartsWith($artifacts, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture output must be beneath repository artifacts.' }
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory; fixtures never overwrite an existing build.' }
if (!(Test-Path -LiteralPath $UnityEditor -PathType Leaf)) { throw "Missing Unity Editor: $UnityEditor" }
if (!$SkipBuild) {
    foreach ($project in @('Anomaly.AssetValidation/Anomaly.AssetValidation.csproj','tools/AssetPackPolicy/AssetPackPolicy.csproj')) {
        & dotnet build (Join-Path $repository $project) -c Release -p:DeployLocal=false --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
}
$projectRoot = Join-Path $output 'unity-project'
$editor = Join-Path $projectRoot 'Assets/Editor'
$dependencies = Join-Path $editor 'ValidationDependencies'
foreach ($directory in @($dependencies, (Join-Path $projectRoot 'Assets/TransferSources'), (Join-Path $projectRoot 'Packages'), (Join-Path $projectRoot 'ProjectSettings'))) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
# Copy the versioned production exporter and its validated policy dependencies.
# The external authoring project supplies reference inputs only and is not opened.
Copy-Item -LiteralPath (Join-Path $repository 'tools/AssetPackPolicy/Unity/AnomalyAssetPackExporter.cs') -Destination (Join-Path $editor 'AssetPackExporter.cs')
Copy-Item -LiteralPath (Join-Path $AuthoringProject 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $projectRoot 'ProjectSettings')
Copy-Item -LiteralPath (Join-Path $repository 'Anomaly.AssetValidation/bin/Release/netstandard2.0/Anomaly.AssetValidation.dll') -Destination $dependencies
Copy-Item -LiteralPath (Join-Path $repository 'tools/AssetPackPolicy/bin/Release/netstandard2.0/Anomaly.AssetPackPolicy.dll') -Destination $dependencies
Copy-Item -LiteralPath (Join-Path $AuthoringProject 'Assets/Editor/ValidationDependencies/AssetsTools.NET.dll') -Destination $dependencies
'{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.audio":"1.0.0"}}' |
    Set-Content -LiteralPath (Join-Path $projectRoot 'Packages/manifest.json') -Encoding utf8
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class TransferTextureFixture {
    // Deterministic high-entropy RGBA pixels, not arbitrary trailing bundle padding.
    public static void Write(string path, uint seed) {
        const int width = 1024, height = 2040;
        using (var stream = File.Create(path)) {
            byte[] header = new byte[18];
            header[2] = 2; header[12] = (byte)(width & 255); header[13] = (byte)(width >> 8);
            header[14] = (byte)(height & 255); header[15] = (byte)(height >> 8);
            header[16] = 32; header[17] = 0x28;
            stream.Write(header, 0, header.Length);
            byte[] row = new byte[width * 4];
            uint state = seed;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < row.Length; x += 4) {
                    state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                    row[x] = (byte)state; row[x+1] = (byte)(state >> 8);
                    row[x+2] = (byte)(state >> 16); row[x+3] = (byte)(state >> 24);
                }
                stream.Write(row, 0, row.Length);
            }
        }
    }
}
'@
$records = @()
for ($pack = 0; $pack -lt $PackCount; $pack++) {
    $name = 'bulk-{0:d2}' -f $pack
    $assets = @()
    for ($texture = 0; $texture -lt 4; $texture++) {
        $relative = "Assets/TransferSources/$name-$texture.tga"
        [TransferTextureFixture]::Write((Join-Path $projectRoot $relative), [uint32](0x61410001 + $pack * 4 + $texture))
        $assets += [ordered]@{Address="mods/network_transfer/textures/$name-$texture";Kind='Texture2D';SourcePath=$relative}
    }
    $export = Join-Path $output "exports/$name"
    New-Item -ItemType Directory -Path $export -Force | Out-Null
    $configPath = Join-Path $export 'pack-config.json'
    [ordered]@{PackId="network_transfer:$name";Namespace='network_transfer';BundleFile="$name.bundle";LocalOnly=$false;Assets=$assets} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $configPath -Encoding utf8
    $log = Join-Path $export 'unity-pack-build.log'
    $arguments = @('-batchmode','-nographics','-quit','-projectPath',('"'+$projectRoot+'"'),'-buildTarget','Win64',
        '-executeMethod','AssetPackExporter.Build','-anomalyPack',('"'+$configPath+'"'),'-anomalyOutput',('"'+$export+'"'),'-logFile',('"'+$log+'"'))
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    while (!$process.WaitForExit(1000)) { }
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -SimpleMatch 'ANOMALY_PACK_EXPORT PASS' -Quiet)) {
        throw "Unity fixture export failed: $log"
    }
    $indexPath = Join-Path $export "$name.index.json"
    $index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
    if ($index.BundleBytes -gt 64MB -or $index.BundleBytes -lt 31MB) { throw "Unexpected fixture bundle size: $($index.BundleBytes)" }
    $records += [pscustomobject]@{PackId=$index.PackId;IndexPath=$indexPath;BundlePath=(Join-Path $export $index.BundleFile);
        BundleBytes=[long]$index.BundleBytes;BundleSha256=$index.BundleSha256;DecodedBytes=[long]$index.DecodedBytes}
}
. (Join-Path $PSScriptRoot '../ContentPackFixture.ps1')
$cases = @()
foreach ($count in @(1,4,8) | Where-Object { $_ -le $PackCount }) {
    $caseRoot = Join-Path $output "case-$($count * 32)/ContentPacks"
    $caseRecords = @($records | Select-Object -First $count)
    if ($BasePackIndexPath) {
        $baseIndex = Get-Content -Raw -LiteralPath $BasePackIndexPath | ConvertFrom-Json
        $caseRecords += [pscustomobject]@{PackId=$baseIndex.PackId;IndexPath=[IO.Path]::GetFullPath($BasePackIndexPath);
            BundlePath=(Join-Path (Split-Path ([IO.Path]::GetFullPath($BasePackIndexPath))) $baseIndex.BundleFile);
            BundleBytes=[long]$baseIndex.BundleBytes;BundleSha256=$baseIndex.BundleSha256;DecodedBytes=[long]$baseIndex.DecodedBytes}
    }
    foreach ($record in $caseRecords) {
        foreach ($entry in @(Get-AssetContentPackFixtureEntries -IndexPath $record.IndexPath -BundlePath $record.BundlePath)) {
            $target = Join-Path $caseRoot $entry.path
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            if ($null -eq $entry.source) { [IO.File]::WriteAllText($target, $entry.content, [Text.UTF8Encoding]::new($false)) }
            else { Copy-Item -LiteralPath $entry.source -Destination $target }
        }
    }
    $bootstrap = Join-Path $caseRoot 'network_transfer/bootstrap'
    New-Item -ItemType Directory -Path (Join-Path $bootstrap 'scripts') -Force | Out-Null
    '{"SchemaVersion":1,"PackId":"network_transfer:bootstrap","Version":"1.0.0","Scripts":{"PackId":"network_transfer:bootstrap","EntryPoint":"scripts/main.lua"}}' |
        Set-Content -LiteralPath (Join-Path $bootstrap 'pack.json') -Encoding utf8
    ('Output:Log("NETWORK_TRANSFER_SCRIPT_STARTED case=' + ($count * 32) + '"); Events.Client.Ready.Add(function(_) Output:Log("NETWORK_TRANSFER_SCRIPT_READY") end)') |
        Set-Content -LiteralPath (Join-Path $bootstrap 'scripts/main.lua') -Encoding utf8
    $expected = [ordered]@{NominalMiB=$count*32;AssetPacks=@($caseRecords | ForEach-Object {
        [ordered]@{PackId=$_.PackId;BundleSha256=$_.BundleSha256;BundleBytes=$_.BundleBytes;DecodedBytes=$_.DecodedBytes;
            IndexSha256=(Get-FileHash -LiteralPath $_.IndexPath -Algorithm SHA256).Hash}
    });ScriptPackId='network_transfer:bootstrap';ScriptSha256=(Get-FileHash -LiteralPath (Join-Path $bootstrap 'scripts/main.lua') -Algorithm SHA256).Hash}
    $expected | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path (Split-Path $caseRoot) 'expected-content.json') -Encoding utf8
    & (Join-Path $PSScriptRoot 'Pin-Content.ps1') -ContentPackDirectory $caseRoot -ExpectedContentPath (Join-Path (Split-Path $caseRoot) 'expected-content.json')
    $payloadFiles = @(Get-ChildItem -LiteralPath $caseRoot -File -Recurse | Where-Object Name -ne 'pack.json')
    $sourceBytes = [long](($payloadFiles | Measure-Object Length -Sum).Sum)
    if ($sourceBytes -ge 256MB) { throw 'Fixture must leave room below the 256 MiB manifest limit for generated script metadata.' }
    $cases += [pscustomobject]@{NominalMiB=$count*32;ContentPackDirectory=$caseRoot;SourcePayloadBytes=$sourceBytes;BundleCount=$count;
        ExpectedCold=$true;Note='Actual wire bytes come from the live manifest; script index is generated by the server.'}
}
[ordered]@{SchemaVersion=1;TextureWidth=1024;TextureHeight=2040;TextureFormat='RGBA32';SeedBase='61410001';
    UnityEditor=$UnityEditor;ExporterSha256=(Get-FileHash -LiteralPath (Join-Path $editor 'AssetPackExporter.cs')).Hash;
    Packs=$records;Cases=$cases} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'fixtures.json') -Encoding utf8
Write-Output (Join-Path $output 'fixtures.json')
