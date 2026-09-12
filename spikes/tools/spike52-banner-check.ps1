# Spike S5.2 (round 2): показать ли баннер при ShowBanner=0, записанном
# ИЗ packaged-приложения. Пара скринов зоны тостов + диф (по образцу spike-e1e).
# Порядок: сами управляем ключом снаружи (reg add/delete), скрипт только
# измеряет: base1 -> base2 -> тост -> t500 -> t1500 -> числа диффа.
param([string]$Tag = 'r2')
$ErrorActionPreference = 'Continue'
Add-Type -MemberDefinition '
[DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
' -Name DpiFix -Namespace NativeS52
[void][NativeS52.DpiFix]::SetProcessDpiAwareness(2)
Add-Type -AssemblyName System.Drawing

$b = @{ Width = [NativeS52.DpiFix]::GetSystemMetrics(0); Height = [NativeS52.DpiFix]::GetSystemMetrics(1) }
$w = 700; $h = 340   # физическая зона тостов (~460x220 логических при 150%)

function ShotPath([string]$t) { return "C:\devs\tmp\s52-$t.bmp" }
function Shot([string]$t) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(($b.Width - $w), ($b.Height - $h - 90), 0, 0, (New-Object System.Drawing.Size($w, $h)))
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

function SendToast([string]$title) {
    [void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]
    [void][Windows.UI.Notifications.ToastNotification,Windows.UI.Notifications,ContentType=WindowsRuntime]
    [void][Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom,ContentType=WindowsRuntime]
    $x = New-Object Windows.Data.Xml.Dom.XmlDocument
    $x.LoadXml("<toast><visual><binding template=`"ToastGeneric`"><text>$title</text><text>s52 banner-check</text></binding></visual></toast>")
    $t = New-Object Windows.UI.Notifications.ToastNotification($x)
    $aumid = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($aumid).Show($t)
}

Shot "$Tag-base1"
Start-Sleep -Milliseconds 300
Shot "$Tag-base2"
$noise = DiffSampled (ShotPath "$Tag-base1") (ShotPath "$Tag-base2")
SendToast "s52 $Tag"
Start-Sleep -Milliseconds 500
Shot "$Tag-t500"
Start-Sleep -Milliseconds 1000
Shot "$Tag-t1500"
$d1 = DiffSampled (ShotPath "$Tag-base2") (ShotPath "$Tag-t500")
$d2 = DiffSampled (ShotPath "$Tag-base2") (ShotPath "$Tag-t1500")
[pscustomobject]@{ Tag = $Tag; Noise = $noise; D500 = $d1; D1500 = $d2 } | Format-Table -AutoSize
Remove-Item C:\devs\tmp\s52-*.bmp -ErrorAction SilentlyContinue
