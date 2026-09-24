[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputPng,
    [Parameter(Mandatory)][string]$OutputRgba32
)
$ErrorActionPreference = 'Stop'
$sourcePath = [IO.Path]::GetFullPath($InputPng)
$targetPath = [IO.Path]::GetFullPath($OutputRgba32)
if ($sourcePath -eq $targetPath -or [IO.Path]::GetExtension($targetPath) -ne '.rgba32') {
    throw 'Choose a separate .rgba32 output; source images are never modified.'
}
Add-Type -AssemblyName System.Drawing.Common
$bitmap = [Drawing.Bitmap]::new($sourcePath)
try {
    if ($bitmap.Width -lt 1 -or $bitmap.Width -gt 4096 -or $bitmap.Height -lt 1 -or $bitmap.Height -gt 4096) {
        throw 'Image dimensions exceed 4096x4096.'
    }
    $stream = [IO.File]::Open($targetPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes("ARGBA32`0"))
        $writer.Write([int]$bitmap.Width)
        $writer.Write([int]$bitmap.Height)
        # Unity SetPixels32 expects bottom-up rows; this matches the former PNG loader exactly.
        for ($y = $bitmap.Height - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $pixel = $bitmap.GetPixel($x, $y)
                $writer.Write([byte]$pixel.R)
                $writer.Write([byte]$pixel.G)
                $writer.Write([byte]$pixel.B)
                $writer.Write([byte]$pixel.A)
            }
        }
    } finally { $writer.Dispose() }
} finally { $bitmap.Dispose() }
Write-Output "Converted offline: $targetPath"
