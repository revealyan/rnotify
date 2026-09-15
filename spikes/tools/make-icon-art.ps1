# Три кандидата иконки rnotify (S6.5): колокол — монохром Win11-стиль,
# фиолетовый бейдж, тёмный бейдж. 256px PNG в %TEMP%, открыть для выбора.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-Canvas([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    return @($bmp, $g)
}

function Draw-Bell($g, [float]$scale, [System.Drawing.Brush]$brush) {
    # Колокол в координатах 512-мастера: купол-дуга, бока-кривые, юбка, база, язычок.
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $s = $scale
    # старт у левой юбки
    [void]$p.AddBezier(([float](86*$s)), ([float](346*$s)), ([float](150*$s)), ([float](210*$s)), ([float](170*$s)), ([float](190*$s)), ([float](256*$s)), ([float](190*$s)))
    # купол вправо
    [void]$p.AddBezier(([float](342*$s)), ([float](190*$s)), ([float](362*$s)), ([float](210*$s)), ([float](426*$s)), ([float](346*$s)), ([float](426*$s)), ([float](346*$s)))
    # юбка: вниз и влево по нижней дуге
    [void]$p.AddLine(([float](426*$s)), ([float](346*$s)), ([float](402*$s)), ([float](374*$s)))
    [void]$p.AddBezier(([float](330*$s)), ([float](400*$s)), ([float](182*$s)), ([float](400*$s)), ([float](110*$s)), ([float](374*$s)), ([float](86*$s)), ([float](346*$s)))
    [void]$p.CloseFigure()
    $g.FillPath($brush, $p)
    $p.Dispose()

    # база-полочка
    $bar = New-Object System.Drawing.Drawing2D.GraphicsPath
    [void]$bar.AddArc(([float](120*$s)), ([float](414*$s)), ([float](52*$s)), ([float](52*$s)), 180, 180)
    [void]$bar.AddLine(([float](146*$s)), ([float](440*$s)), ([float](366*$s)), ([float](440*$s)))
    [void]$bar.AddArc(([float](340*$s)), ([float](414*$s)), ([float](52*$s)), ([float](52*$s)), 0, 180)
    [void]$bar.AddLine(([float](366*$s)), ([float](440*$s)), ([float](146*$s)), ([float](440*$s)))
    [void]$bar.CloseFigure()
    $g.FillPath($brush, $bar)
    $bar.Dispose()

    # язычок
    $g.FillEllipse($brush, ([float](226*$s)), ([float](452*$s)), ([float](60*$s)), ([float](60*$s)))
}

function Save-Badge($path, [int]$size, [System.Drawing.Color]$badge, [System.Drawing.Brush]$bellBrush) {
    $c = New-Canvas $size
    $bmp = $c[0]; $g = $c[1]
    if ($badge.A -gt 0) {
        $r = [int]($size * 0.22)
        $badgePath = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $badgePath.AddArc(0, 0, 2*$r, 2*$r, 180, 90)
        $badgePath.AddArc($size - 2*$r, 0, 2*$r, 2*$r, 270, 90)
        $badgePath.AddArc($size - 2*$r, $size - 2*$r, 2*$r, 2*$r, 0, 90)
        $badgePath.AddArc(0, $size - 2*$r, 2*$r, 2*$r, 90, 90)
        $badgePath.CloseFigure()
        $g.FillPath((New-Object System.Drawing.SolidBrush $badge), $badgePath)
        $badgePath.Dispose()
    }
    Draw-Bell $g ($size / 512.0) $bellBrush
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

$dir = Join-Path $env:TEMP 'rnotify-icons'
New-Item $dir -ItemType Directory -Force | Out-Null
$white = [System.Drawing.Brushes]::White
$purple = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 124, 58, 237))

# A: монохром белый на прозрачном (стиль системных треевых Win11)
Save-Badge (Join-Path $dir 'a-mono-white.png') 256 ([System.Drawing.Color]::FromArgb(0,0,0,0)) $white
# B: фиолетовый бейдж + белый колокол
Save-Badge (Join-Path $dir 'b-purple-badge.png') 256 ([System.Drawing.Color]::FromArgb(255, 124, 58, 237)) $white
# C: тёмный бейдж (материал карточки) + фиолетовый колокол
Save-Badge (Join-Path $dir 'c-dark-badge.png') 256 ([System.Drawing.Color]::FromArgb(255, 40, 40, 43)) $purple
Get-ChildItem $dir
