[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AuthoringProject,
    [Parameter(Mandatory)] [string] $PackConfigPath,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [string] $UnityEditor = 'C:\Program Files\6000.0.43f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath($AuthoringProject)
$config = [IO.Path]::GetFullPath($PackConfigPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$versionFile = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
if (!(Test-Path -LiteralPath $versionFile -PathType Leaf) -or
    !(Select-String -LiteralPath $versionFile -SimpleMatch 'm_EditorVersion: 6000.0.43f1' -Quiet)) {
    throw 'Choose a Unity 6000.0.43f1 authoring project.'
}
if (!(Test-Path -LiteralPath $UnityEditor -PathType Leaf)) { throw "Unity Editor missing: $UnityEditor" }
if (!(Test-Path -LiteralPath $config -PathType Leaf)) { throw "Pack configuration missing: $config" }
$source = Join-Path $PSScriptRoot 'AssetPackExporter.cs'
$dependencies = @('Anomaly.AssetValidation.dll','Anomaly.AssetPackPolicy.dll','AssetsTools.NET.dll')
foreach ($inputPath in @($source) + @($dependencies | ForEach-Object { Join-Path $PSScriptRoot "bin/$_" })) {
    if (!(Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "Authoring SDK input missing: $inputPath" }
}
$editor = Join-Path $project 'Assets/Editor'
$editorDependencies = Join-Path $editor 'ValidationDependencies'
New-Item -ItemType Directory -Path $editorDependencies -Force | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $editor 'AssetPackExporter.cs') -Force
foreach ($dependency in $dependencies) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "bin/$dependency") -Destination $editorDependencies -Force
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$log = Join-Path $output 'unity-pack-build.log'
$arguments = @('-batchmode','-nographics','-quit','-projectPath',('"'+$project+'"'),'-buildTarget','Win64',
    '-executeMethod','AssetPackExporter.Build','-anomalyPack',('"'+$config+'"'),
    '-anomalyOutput',('"'+$output+'"'),'-logFile',('"'+$log+'"'))
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -SimpleMatch 'ANOMALY_PACK_EXPORT PASS' -Quiet)) {
    throw "Unity did not publish a validated pack. Inspect $log"
}
$global:LASTEXITCODE = 0
Write-Output "Validated schema-2 pack exported to $output"
