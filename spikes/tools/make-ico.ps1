# Генерация rnotify.ico из Square150x150Logo.png — классические BMP-кадры
# (32bpp BGRA + AND-маска): PNG-кадры LoadImage не читает (грабля S6.1 —
# «место в трее есть, картинка пустая»).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$src = Join-Path $PSScriptRoot '..\..\src\rnotify\Assets\Square150x150Logo.png'
$dst = Join-Path $PSScriptRoot '..\..\src\rnotify\Assets\rnotify.ico'
$sizes = @(16, 24, 32, 48)

$source = [System.Drawing.Image]::FromFile($src)
$frames = @()
try {
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($source, 0, 0, $size, $size)
        $g.Dispose()

        $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
        $bd = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $len = [Math]::Abs($bd.Stride) * $size
        $pixels = New-Object byte[] $len
        [System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $pixels, 0, $len)
        $bmp.UnlockBits($bd)
        $bmp.Dispose()

        # AND-маска нулями (прозрачность живёт в альфе 32bpp), строки добиты до 4 байт
        $maskRow = [int][Math]::Ceiling($size / 8.0)
        $maskStride = [int][Math]::Ceiling($maskRow / 4.0) * 4
        $mask = New-Object byte[] ($maskStride * $size)

        # BITMAPINFOHEADER: biHeight = 2×size (XOR+AND в одном растре)
        $ms = New-Object System.IO.MemoryStream
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([uint32]40)            # biSize
        $bw.Write([int32]$size)          # biWidth
        $bw.Write([int32]($size * 2))    # biHeight
        $bw.Write([uint16]1)             # biPlanes
        $bw.Write([uint16]32)            # biBitCount
        $bw.Write([uint32]0)             # biCompression
        $bw.Write([uint32]($len + $mask.Length)) # biSizeImage
        $bw.Write([int32]0)              # biXPelsPerMeter
        $bw.Write([int32]0)              # biYPelsPerMeter
        $bw.Write([uint32]0)             # biClrUsed
        $bw.Write([uint32]0)             # biClrImportant
        $bw.Write($pixels)
        $bw.Write($mask)
        $bw.Close()

        $frames += ,@($size, $ms.ToArray())
    }
}
finally {
    $source.Dispose()
}

$fs = [System.IO.File]::Create($dst)
$ow = New-Object System.IO.BinaryWriter $fs
try {
    $ow.Write([uint16]0)      # reserved
    $ow.Write([uint16]1)      # type: icon
    $ow.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $size = $frame[0]
        $bytes = $frame[1]
        $ow.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $ow.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $ow.Write([byte]0)    # colors
        $ow.Write([byte]0)    # reserved
        $ow.Write([uint16]1)  # planes
        $ow.Write([uint16]32) # bit count
        $ow.Write([uint32]$bytes.Length)
        $ow.Write([uint32]$offset)
        $offset += $bytes.Length
    }
    foreach ($frame in $frames) {
        $ow.Write($frame[1])
    }
}
finally {
    $ow.Close()
    $fs.Close()
}
"ico: $dst ($((Get-Item $dst).Length) bytes, BMP-кадры)"
