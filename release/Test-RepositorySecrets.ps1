[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$secretRepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$privateKeyMarkers = @(
    ('BEGIN' + ' PRIVATE KEY'),
    ('BEGIN' + ' EC PRIVATE KEY'),
    ('BEGIN' + ' RSA PRIVATE KEY'),
    ('BEGIN' + ' OPENSSH PRIVATE KEY')
)

Push-Location $secretRepositoryRoot
try {
    foreach ($marker in $privateKeyMarkers) {
        $matches = & rg -n -F --hidden `
            --glob '!.git/**' `
            --glob '!**/bin/**' `
            --glob '!**/obj/**' `
            --glob '!artifacts/**' `
            --glob '!**/TestResults/**' `
            $marker .
        $scanExitCode = $LASTEXITCODE
        if ($scanExitCode -eq 0) {
            $matches | Write-Host
            throw 'Private-key material was found in the repository.'
        }
        if ($scanExitCode -ne 1) {
            throw "Secret scan failed to execute (rg exit code $scanExitCode)."
        }
    }

    $global:LASTEXITCODE = 0
    Write-Host 'Repository private-key marker scan passed.'
}
finally {
    Pop-Location
}
