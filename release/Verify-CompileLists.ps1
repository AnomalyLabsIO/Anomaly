[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$compileRepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$explicitProjects = @(
    'Anomaly.Client/Anomaly.Client.csproj',
    'Anomaly.Server/Anomaly.Server.csproj',
    'Anomaly.Shared/Anomaly.Shared.csproj'
)

foreach ($projectRelativePath in $explicitProjects) {
    $projectPath = Join-Path $compileRepositoryRoot $projectRelativePath
    $projectDirectory = Split-Path -Parent $projectPath
    [xml] $projectXml = Get-Content -Raw -LiteralPath $projectPath
    $declared = @($projectXml.SelectNodes("//*[local-name()='Compile']") | ForEach-Object {
        $_.Include.Replace('/', '\')
    })
    $duplicateIncludes = @($declared | Group-Object | Where-Object Count -gt 1)
    if ($duplicateIncludes.Count -gt 0) {
        throw ("{0} contains duplicate Compile entries: {1}" -f $projectRelativePath,
            (($duplicateIncludes.Name | Sort-Object) -join ', '))
    }

    $sourceFiles = @(Get-ChildItem -LiteralPath $projectDirectory -Filter '*.cs' -File -Recurse | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    } | ForEach-Object {
        $trimChars = [char[]]@(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
        $projectPrefix = $projectDirectory.TrimEnd($trimChars) + [System.IO.Path]::DirectorySeparatorChar
        $_.FullName.Substring($projectPrefix.Length).Replace('/', '\')
    })

    $missing = @($sourceFiles | Where-Object { $_ -notin $declared } | Sort-Object)
    # Linked source files can live in another project; verify their declared path too.
    $stale = @($declared | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $projectDirectory $_) -PathType Leaf)
    } | Sort-Object)
    if ($missing.Count -gt 0 -or $stale.Count -gt 0) {
        throw ("Compile list mismatch in {0}. Missing: [{1}] Stale: [{2}]" -f
            $projectRelativePath, ($missing -join ', '), ($stale -join ', '))
    }
}

Write-Host 'Explicit C# compile lists are complete and duplicate-free.'
