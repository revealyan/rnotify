# Замер цвета нативного баннера (S6.4, материал карточек): точечно вернуть
# ShowBanner для AUMID powershell → тост → два снимка зоны (до/после) →
# средний цвет bbox диффа + фон. DPI-aware (канон §10a). В конце вернуть blanket.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class DpiAware
{
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
}
'@
[void][DpiAware]::SetProcessDPIAware()

Add-Type -AssemblyName System.Windows.Forms
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$w = 640; $h = 230
$x = $wa.Width - $w - 6
$y = $wa.Height - $h - 6

function Capture-Zone {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    return $bmp
}

function Avg-Color($bmp, $rect) {
    $r = 0.0; $gr = 0.0; $b = 0.0; $n = 0
    for ($px = $rect.X; $px -lt ($rect.X + $rect.Width); $px += 2) {
        for ($py = $rect.Y; $py -lt ($rect.Y + $rect.Height); $py += 2) {
            $c = $bmp.GetPixel($px, $py)
            $r += $c.R; $gr += $c.G; $b += $c.B; $n++
        }
    }
    return @{ R = [int]($r / $n); G = [int]($gr / $n); B = [int]($b / $n); N = $n }
}

# 1) фон зоны без баннера
$bg = Capture-Zone

# 2) включить нативный баннер ТОЛЬКО для powershell
$root = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings'
$aumid = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
$appkey = Join-Path $root $aumid
$hadKey = Test-Path $appkey
if (-not $hadKey) { New-Item $appkey -Force | Out-Null }
Set-ItemProperty $appkey -Name ShowBanner -Type DWord -Value 1

try {
    # 3) тост и снимок с баннером
    & (Join-Path $PSScriptRoot 'send-test-toast.ps1') -Title 'measure banner' -Body 'color probe' | Out-Null
    Start-Sleep -Milliseconds 2500
    $shot = Capture-Zone

    # 4) bbox диффа
    $minX = $w; $minY = $h; $maxX = 0; $maxY = 0; $diffPx = 0
    for ($px = 0; $px -lt $w; $px += 2) {
        for ($py = 0; $py -lt $h; $py += 2) {
            $c1 = $bg.GetPixel($px, $py); $c2 = $shot.GetPixel($px, $py)
            if ([Math]::Abs($c1.R - $c2.R) -gt 12 -or [Math]::Abs($c1.G - $c2.G) -gt 12 -or [Math]::Abs($c1.B - $c2.B) -gt 12) {
                $diffPx++
                if ($px -lt $minX) { $minX = $px }; if ($px -gt $maxX) { $maxX = $px }
                if ($py -lt $minY) { $minY = $py }; if ($py -gt $maxY) { $maxY = $py }
            }
        }
    }
    if ($diffPx -lt 100) { "БАННЕРА НЕТ (дифф $diffPx px)"; return }
    $bw = $maxX - $minX; $bh = $maxY - $minY
    "bbox: ${bw}x${bh} @ ($minX,$minY), дифф $diffPx px"

    # 5) средние: фон и интерьер баннера (без рамок 10px)
    $bgAvg = Avg-Color $bg (New-Object System.Drawing.Rectangle(($minX + 10), ($minY + 10), [Math]::Max(1, $bw - 20), [Math]::Max(1, $bh - 20)))
    $bnAvg = Avg-Color $shot (New-Object System.Drawing.Rectangle(($minX + 10), ($minY + 10), [Math]::Max(1, $bw - 20), [Math]::Max(1, $bh - 20)))
    "фон зоны:    RGB($($bgAvg.R),$($bgAvg.G),$($bgAvg.B)) #{0:X2}{1:X2}{2:X2}" -f $bgAvg.R, $bgAvg.G, $bgAvg.B
    "баннер:      RGB($($bnAvg.R),$($bnAvg.G),$($bnAvg.B)) #{0:X2}{1:X2}{2:X2}" -f $bnAvg.R, $bnAvg.G, $bnAvg.B
    # решение замеса при альфе a: card = (bn - bg*(1-a))/a — для a=1.0/0.9
    foreach ($a in @(1.0, 0.9)) {
        $cr = [int](($bnAvg.R - $bgAvg.R * (1 - $a)) / $a)
        $cg = [int](($bnAvg.G - $bgAvg.G * (1 - $a)) / $a)
        $cb = [int](($bnAvg.B - $bgAvg.B * (1 - $a)) / $a)
        "при альфе $($a): RGB($cr,$cg,$cb)"
    }
}
finally {
    # 6) вернуть blanket (значение удалить — до формулы его не было)
    if ($hadKey) { Set-ItemProperty $appkey -Name ShowBanner -Type DWord -Value 0 }
    else { Remove-ItemProperty $appkey -Name ShowBanner -ErrorAction SilentlyContinue }
}
