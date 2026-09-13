# Spike S4.1 (раунд 0): геометрия нативного баннера на этой машине.
# DPI-aware (shcore + физические GetSystemMetrics), зона 760x380 физ.пикс у
# правого-нижнего угла (@90 px от низа экрана — прецедент S5.2, расширенная).
# Порядок: base1 -> base2 (Noise) -> нативный тост -> t500 -> t1800 (баннер
# позже 500 мс — раньше ~1.5 с не мерим, §10a). Bbox изменённых пикселей пары
# base2/t1800 -> позиция/размер баннера на экране. Центр НЕ чистим (§10a),
# foreground печатаем как гейт прогона.
param([string]$Tag = 'r0')
$ErrorActionPreference = 'Continue'
Add-Type -MemberDefinition '
[DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
[DllImport("user32.dll")] public static extern uint GetDpiForSystem();
' -Name DpiFix -Namespace NativeS41
[void][NativeS41.DpiFix]::SetProcessDpiAwareness(2)
Add-Type -AssemblyName System.Drawing

$b = @{ Width = [NativeS41.DpiFix]::GetSystemMetrics(0); Height = [NativeS41.DpiFix]::GetSystemMetrics(1) }
$scale = [NativeS41.DpiFix]::GetDpiForSystem() / 96.0
$w = 760; $h = 380   # физическая зона баннера (карточки 364x124 DIP + запас)
$anchorY = 90        # низ зоны над низом экрана (прецедент S5.2)

$fg = [NativeS41.DpiFix]::GetForegroundWindow()
$sb = New-Object System.Text.StringBuilder 256
[void][NativeS41.DpiFix]::GetWindowText($fg, $sb, 256)

function ShotPath([string]$t) { return "C:\devs\tmp\s41-$t.bmp" }
function Shot([string]$t) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(($b.Width - $w), ($b.Height - $h - $anchorY), 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $bmp.Save((ShotPath $t), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
}

function DiffSampled([string]$p1, [string]$p2) {
    $fs1 = [System.IO.File]::OpenRead($p1)
    $fs2 = [System.IO.File]::OpenRead($p2)
    if ($fs1.Length -ne $fs2.Length) { $fs1.Close(); $fs2.Close(); return 999999 }
    $buf1 = New-Object byte[] 4096
    $buf2 = New-Object byte[] 4096
    $diff = 0
    while ($fs1.Read($buf1, 0, 4096) -gt 0) {
        [void]$fs2.Read($buf2, 0, 4096)
        for ($i = 0; $i -lt 4096; $i += 211) {
            if ($buf1[$i] -ne $buf2[$i]) { $diff++ }
        }
    }
    $fs1.Close(); $fs2.Close()
    return $diff
}

# Bbox изменённых пикселей (LockBits, допуск 8/канал — переживает мерцание
# курсора/антиалиасинг). Возврат: счётчик + min/max по X/Y в координатах зоны.
function DiffBbox([string]$p1, [string]$p2) {
    $bmp1 = [System.Drawing.Bitmap]::FromFile($p1)
    $bmp2 = [System.Drawing.Bitmap]::FromFile($p2)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $bmp1.Width, $bmp1.Height)
    $fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    $d1 = $bmp1.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $fmt)
    $d2 = $bmp2.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $fmt)
    $stride = [Math]::Abs($d1.Stride)
    $len = $stride * $bmp1.Height
    $b1 = New-Object byte[] $len
    $b2 = New-Object byte[] $len
    [System.Runtime.InteropServices.Marshal]::Copy($d1.Scan0, $b1, 0, $len)
    [System.Runtime.InteropServices.Marshal]::Copy($d2.Scan0, $b2, 0, $len)
    $bmp1.UnlockBits($d1)
    $bmp2.UnlockBits($d2)
    $minX = -1; $maxX = -1; $minY = -1; $maxY = -1; $cnt = 0
    for ($y = 0; $y -lt $bmp1.Height; $y++) {
        $row = $y * $stride
        for ($x = 0; $x -lt $bmp1.Width; $x++) {
            $o = $row + $x * 4
            if ([Math]::Abs($b1[$o] - $b2[$o]) -gt 8 -or [Math]::Abs($b1[$o + 1] - $b2[$o + 1]) -gt 8 -or [Math]::Abs($b1[$o + 2] - $b2[$o + 2]) -gt 8) {
                $cnt++
                if ($minX -lt 0 -or $x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($minY -lt 0) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    $bmp1.Dispose()
    $bmp2.Dispose()
    return @{ Count = $cnt; MinX = $minX; MaxX = $maxX; MinY = $minY; MaxY = $maxY }
}

function SendToast([string]$title) {
    [void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]
    [void][Windows.UI.Notifications.ToastNotification,Windows.UI.Notifications,ContentType=WindowsRuntime]
    [void][Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom,ContentType=WindowsRuntime]
    $x = New-Object Windows.Data.Xml.Dom.XmlDocument
    $x.LoadXml("<toast><visual><binding template=`"ToastGeneric`"><text>$title</text><text>s41 banner-zone</text></binding></visual></toast>")
    $t = New-Object Windows.UI.Notifications.ToastNotification($x)
    $aumid = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($aumid).Show($t)
}

Shot "$Tag-base1"
Start-Sleep -Milliseconds 300
Shot "$Tag-base2"
$noise = DiffSampled (ShotPath "$Tag-base1") (ShotPath "$Tag-base2")
SendToast "s41 $Tag"
Start-Sleep -Milliseconds 500
Shot "$Tag-t500"
Start-Sleep -Milliseconds 1300
Shot "$Tag-t1800"
$d500 = DiffSampled (ShotPath "$Tag-base2") (ShotPath "$Tag-t500")
$d1800 = DiffSampled (ShotPath "$Tag-base2") (ShotPath "$Tag-t1800")
$bbox = DiffBbox (ShotPath "$Tag-base2") (ShotPath "$Tag-t1800")

# Координаты зоны -> координаты экрана (физические) + пересчёт в DIP.
$sx = $b.Width - $w; $sy = $b.Height - $h - $anchorY
$banner = $null
if ($bbox.MinX -ge 0) {
    $banner = @{
        X = $sx + $bbox.MinX; Y = $sy + $bbox.MinY
        W = $bbox.MaxX - $bbox.MinX + 1; H = $bbox.MaxY - $bbox.MinY + 1
    }
}

[pscustomobject]@{
    Tag          = $Tag
    Screen       = "$($b.Width)x$($b.Height) phys, scale $scale"
    Foreground   = $sb.ToString()
    Noise        = $noise
    D500         = $d500
    D1800        = $d1800
    DiffPixels   = $bbox.Count
    BannerPhys   = if ($banner) { "$($banner.X),$($banner.Y) $($banner.W)x$($banner.H)" } else { '-' }
    BannerDip    = if ($banner) { "{0:N0},{1:N0} {2:N0}x{3:N0}" -f ($banner.X / $scale), ($banner.Y / $scale), ($banner.W / $scale), ($banner.H / $scale) } else { '-' }
} | Format-List
Remove-Item C:\devs\tmp\s41-*.bmp -ErrorAction SilentlyContinue
