# Генерация rnotify.ico из Square150x150Logo.png (PNG-кадры 16/24/32/48 —
# формат валиден с Vista+). Одноразовый инструментasset-провенанса.
# PS 5.1: System.Drawing, без discards.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$src = Join-Path $PSScriptRoot '..\..\src\rnotify\Assets\Square150x150Logo.png'
$dst = Join-Path $PSScriptRoot '..\..\src\rnotify\Assets\rnotify.ico'
$sizes = @(16, 24, 32, 48)

$source = [System.Drawing.Image]::FromFile($src)
try {
    $frames = @()
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($source, 0, 0, $size, $size)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $frames += ,@($size, $ms.ToArray())
    }
}
finally {
    $source.Dispose()
}

$fs = [System.IO.File]::Create($dst)
$bw = New-Object System.IO.BinaryWriter $fs
try {
    $bw.Write([uint16]0)      # reserved
    $bw.Write([uint16]1)      # type: icon
    $bw.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $size = $frame[0]
        $bytes = $frame[1]
        $bw.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $bw.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $bw.Write([byte]0)    # colors
        $bw.Write([byte]0)    # reserved
        $bw.Write([uint16]1)  # planes
        $bw.Write([uint16]32) # bit count
        $bw.Write([uint32]$bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $bytes.Length
    }
    foreach ($frame in $frames) {
        $bw.Write($frame[1])
    }
}
finally {
    $bw.Close()
    $fs.Close()
}
"ico: $dst ($((Get-Item $dst).Length) bytes)"
