[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $UpstreamSourceDirectory,
    [string] $Destination
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'third_party'))
if (-not $Destination) { $Destination = Join-Path $allowedRoot 'MoonSharp' }
$targetDirectory = [IO.Path]::GetFullPath($Destination)
if (-not $targetDirectory.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Destination must be a new directory below this repository third_party directory.'
}
if (Test-Path -LiteralPath $targetDirectory) { throw "Destination already exists: $targetDirectory" }

# Do not follow an existing junction/symlink to write outside the intended tree.
$ancestor = [IO.DirectoryInfo]::new($targetDirectory).Parent
while ($null -ne $ancestor) {
    if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Destination ancestor is a reparse point: $($ancestor.FullName)"
    }
    if ($ancestor.FullName -eq $repoRoot) { break }
    $ancestor = $ancestor.Parent
}

$sourceDirectory = (Resolve-Path -LiteralPath $UpstreamSourceDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'upstream-source-manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    $sourcePath = Join-Path $sourceDirectory $entry.Path
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $entry.SHA256) {
        throw "Upstream source differs from pinned MoonSharp fbd03678: $($entry.Path)"
    }
}

# All inputs are verified before creating anything. Never overwrite a local fork.
[void][IO.Directory]::CreateDirectory((Join-Path $targetDirectory 'src'))
foreach ($entry in $manifest) {
    $targetPath = Join-Path (Join-Path $targetDirectory 'src') $entry.Path
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($targetPath))
    Copy-Item -LiteralPath (Join-Path $sourceDirectory $entry.Path) -Destination $targetPath
}
foreach ($name in @('Anomaly.MoonSharp.Interpreter.csproj', 'LICENSE', 'upstream-source-manifest.json', 'anomaly-hardening.patch')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $targetDirectory $name)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Fork-README.md') -Destination (Join-Path $targetDirectory 'README.md')
$relativeTarget = [IO.Path]::GetRelativePath($repoRoot, $targetDirectory).Replace('\', '/')
$patchPath = Join-Path $PSScriptRoot 'anomaly-hardening.patch'
& git -C $repoRoot apply --check --ignore-space-change "--directory=$relativeTarget" -- $patchPath
if ($LASTEXITCODE -ne 0) { throw "Patch validation failed; incomplete restore remains at $targetDirectory" }
& git -C $repoRoot apply --ignore-space-change "--directory=$relativeTarget" -- $patchPath
if ($LASTEXITCODE -ne 0) { throw "Patch application failed; incomplete restore remains at $targetDirectory" }
Write-Output "Restored pinned MoonSharp with Anomaly hardening to $targetDirectory"
