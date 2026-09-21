[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ReferenceOutput,
    [Parameter(Mandatory = $true)]
    [string] $CandidateOutput
)

$ErrorActionPreference = 'Stop'
$ReferenceOutput = [System.IO.Path]::GetFullPath($ReferenceOutput)
$CandidateOutput = [System.IO.Path]::GetFullPath($CandidateOutput)

foreach ($path in @($ReferenceOutput, $CandidateOutput)) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Release output does not exist: $path"
    }
}

function Get-ReleaseFileInventory([string] $root) {
    $rootWithSeparator = $root.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    return @(Get-ChildItem -LiteralPath $root -File -Recurse | ForEach-Object {
        $relativePath = $_.FullName.Substring($rootWithSeparator.Length).Replace('\', '/')
        [pscustomobject]@{
            Path = $relativePath
            SizeBytes = $_.Length
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    } | Sort-Object Path)
}

$referenceFiles = Get-ReleaseFileInventory $ReferenceOutput
$candidateFiles = Get-ReleaseFileInventory $CandidateOutput
if ($referenceFiles.Count -ne $candidateFiles.Count) {
    throw "Release output file counts differ: $($referenceFiles.Count) != $($candidateFiles.Count)."
}

for ($index = 0; $index -lt $referenceFiles.Count; $index++) {
    $reference = $referenceFiles[$index]
    $candidate = $candidateFiles[$index]
    if ($reference.Path -ne $candidate.Path -or
        $reference.SizeBytes -ne $candidate.SizeBytes -or
        $reference.Sha256 -ne $candidate.Sha256) {
        throw ("Release outputs differ at index {0}: '{1}' versus '{2}'." -f `
            $index, $reference.Path, $candidate.Path)
    }
}

$global:LASTEXITCODE = 0
Write-Host ("Release outputs are byte-for-byte reproducible ({0} files)." -f $referenceFiles.Count)
