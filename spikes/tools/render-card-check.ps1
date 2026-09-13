# Spike S4.1 (раунды 1-2): отрисовалась ли карточка спайк-хоста в зоне
# нативного баннера и не украла ли фокус. Адаптация spike52-banner-check.ps1:
# DPI-aware, зона 760x380 @90 от низа (та же, что у native-banner-zone.ps1 —
# геометрия сравнима), base1 -> base2 (Noise) -> запуск exe «card --ttl N» ->
# t300 (вход анимации) -> t1500 (устойчиво) -> AfterExit (карточка ушла,
# экран вернулся к базе). Foreground-чек до/после — совпал ли HWND.
# Центр НЕ чистим (§10a); тосты rhub в фоне ловим Noise-колонкой.
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [int]$Ttl = 6,
    [string]$Tag = 'run'
)
$ErrorActionPreference = 'Continue'
Add-Type -MemberDefinition '
[DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
' -Name DpiFix -Namespace NativeS41c
[void][NativeS41c.DpiFix]::SetProcessDpiAwareness(2)
Add-Type -AssemblyName System.Drawing

$b = @{ Width = [NativeS41c.DpiFix]::GetSystemMetrics(0); Height = [NativeS41c.DpiFix]::GetSystemMetrics(1) }
$w = 760; $h = 380
$anchorY = 90

function FgInfo {
    $hwnd = [NativeS41c.DpiFix]::GetForegroundWindow()
    $sb = New-Object System.Text.StringBuilder 256
    [void][NativeS41c.DpiFix]::GetWindowText($hwnd, $sb, 256)
    return @{ Hwnd = $hwnd; Title = $sb.ToString() }
}

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

$fgBefore = FgInfo
Shot "$Tag-base1"
Start-Sleep -Milliseconds 300
Shot "$Tag-base2"
$noise = DiffSampled (ShotPath "$Tag-base1") (ShotPath "$Tag-base2")

# Карточка: спайк-хост сам ставит себя в зону нативного баннера.
$p = Start-Process -FilePath $ExePath -ArgumentList "card --ttl $Ttl" -PassThru
Start-Sleep -Milliseconds 300
Shot "$Tag-t300"
$fg300 = FgInfo
Start-Sleep -Milliseconds 1200
Shot "$Tag-t1500"
$fg1500 = FgInfo
$exited = $p.WaitForExit((($Ttl + 10)) * 1000)
$exitCode = if ($exited) { $p.ExitCode } else { -1 }
Start-Sleep -Milliseconds 400
Shot "$Tag-texit"

$d300 = DiffSampled (ShotPath "$Tag-base2") (ShotPath "$Tag-t300")
$d1500 = DiffSampled (ShotPath "$Tag-base2") (ShotPath "$Tag-t1500")
$afterExit = DiffSampled (ShotPath "$Tag-base2") (ShotPath "$Tag-texit")

[pscustomobject]@{
    Tag         = $Tag
    Exe         = [System.IO.Path]::GetFileName($ExePath)
    Noise       = $noise
    D300        = $d300
    D1500       = $d1500
    AfterExit   = $afterExit
    FgBefore    = $fgBefore.Title
    FgAt300     = $fg300.Title
    FgAt1500    = $fg1500.Title
    FgStolen    = ($fg300.Hwnd -ne $fgBefore.Hwnd) -or ($fg1500.Hwnd -ne $fgBefore.Hwnd)
    ExitCode    = $exitCode
} | Format-List
Remove-Item C:\devs\tmp\s41-*.bmp -ErrorAction SilentlyContinue
