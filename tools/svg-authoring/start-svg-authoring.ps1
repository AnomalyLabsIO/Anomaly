[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 8765
)

$studioRoot = $PSScriptRoot
$nodeCommand = Get-Command node -ErrorAction SilentlyContinue
if ($null -eq $nodeCommand) {
    Write-Error 'Node.js is required to run Anomaly Vector Studio.'
    exit 1
}

$serverPath = Join-Path $studioRoot 'serve.js'
$studioUrl = "http://127.0.0.1:$Port/"
$serverArguments = @("`"$serverPath`"", '--port', "$Port")
$serverProcess = Start-Process -FilePath $nodeCommand.Source -ArgumentList $serverArguments -WorkingDirectory $studioRoot -WindowStyle Hidden -PassThru

try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($serverProcess.HasExited) {
            break
        }
        try {
            $response = Invoke-WebRequest -Uri $studioUrl -UseBasicParsing -TimeoutSec 1
            if ($response.StatusCode -eq 200) {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 150
        }
    }

    if (-not $ready) {
        throw "The local Vector Studio server did not start on port $Port."
    }

    Start-Process $studioUrl
    Write-Host "Anomaly Vector Studio is open at $studioUrl"
    Write-Host 'Keep this window open while editing. Press Ctrl+C to stop.'
    Wait-Process -Id $serverProcess.Id
}
finally {
    if (-not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id
    }
}
