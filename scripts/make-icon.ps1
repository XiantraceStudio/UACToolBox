# Generates assets/app.ico (multi-size, PNG-compressed entries). ASCII only: this
# file is executed by Windows PowerShell 5.1 which reads BOM-less scripts as ANSI.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root 'assets\app.ico'
New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$pngs = @{}
foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $radius = [Math]::Max(2, [int]($size * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $size - 2
    $path.AddArc(1, 1, $radius, $radius, 180, 90)
    $path.AddArc($d - $radius, 1, $radius, $radius, 270, 90)
    $path.AddArc($d - $radius, $d - $radius, $radius, $radius, 0, 90)
    $path.AddArc(1, $d - $radius, $radius, $radius, 90, 90)
    $path.CloseFigure()
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 61, 61, 61))
    $g.FillPath($background, $path)
    $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $fontSize = [Math]::Max(8, [int]($size * 0.62))
    $font = New-Object System.Drawing.Font('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $text = 'U'
    if ($size -le 20) { $text = 'U' }
    $rect = New-Object System.Drawing.RectangleF(0, ($size * 0.02), $size, $size)
    $g.DrawString($text, $font, $accent, $rect, $format)
    $g.Dispose()
    $memory = New-Object System.IO.MemoryStream
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    $pngs[$size] = $memory.ToArray()
}

$stream = [System.IO.File]::Create($target)
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([UInt16]0)      # reserved
$writer.Write([UInt16]1)      # type: icon
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($size in $sizes) {
    $bytes = $pngs[$size]
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))   # width
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))   # height
    $writer.Write([Byte]0)      # palette
    $writer.Write([Byte]0)      # reserved
    $writer.Write([UInt16]1)    # planes
    $writer.Write([UInt16]32)   # bit count
    $writer.Write([UInt32]$bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($size in $sizes) { $writer.Write($pngs[$size]) }
$writer.Flush(); $writer.Close()
Write-Output "Icon written: $target"
