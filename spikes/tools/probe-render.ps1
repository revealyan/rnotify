# Probe: is the toast DELIVERED and is it RENDERED, with forced settings
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$w = 460; $h = 220

function Shot([string]$tag) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(($b.Width - $w), ($b.Height - $h - 60), 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $bmp.Save("C:\devs\tmp\pr-$tag.bmp", [System.Drawing.Imaging.ImageFormat]::Bmp)
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
        for ($i = 0; $i -lt 4096; $i += 97) {
            if ($buf1[$i] -ne $buf2[$i]) { $diff++ }
        }
    }
    $fs1.Close(); $fs2.Close()
    return $diff
}

$root = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings'
$aumid = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
$appkey = Join-Path $root $aumid

# force explicit state: banners on, DND off
Set-ItemProperty $root -Name NOC_GLOBAL_SETTING_TOASTS_ENABLED -Type DWord -Value 1
Set-ItemProperty $root -Name NOC_GLOBAL_SETTING_DND -Type DWord -Value 0
Remove-ItemProperty $appkey -Name ShowBanner -ErrorAction SilentlyContinue

$before = (Get-ItemProperty $appkey -ErrorAction SilentlyContinue).LastNotificationAddedTime

Shot 'base'
Start-Sleep -Milliseconds 200

[void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]
[void][Windows.UI.Notifications.ToastNotification,Windows.UI.Notifications,ContentType=WindowsRuntime]
[void][Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom,ContentType=WindowsRuntime]
$xdoc = New-Object Windows.Data.Xml.Dom.XmlDocument
$xdoc.LoadXml('<toast><visual><binding template="ToastGeneric"><text>render probe</text><text>delivered and visible?</text></binding></visual></toast>')
$t = New-Object Windows.UI.Notifications.ToastNotification($xdoc)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($aumid).Show($t)

Start-Sleep -Milliseconds 500
Shot 't500'
Start-Sleep -Milliseconds 700
Shot 't1200'
Start-Sleep -Milliseconds 1300
Shot 't2500'

$after = (Get-ItemProperty $appkey -ErrorAction SilentlyContinue).LastNotificationAddedTime

$delivered = 'NO'
if ($null -ne $after) {
    if ($null -eq $before -or $after -gt $before) { $delivered = 'YES' }
}
Write-Output ("delivered to store: {0} (before={1}, after={2})" -f $delivered, $before, $after)
Write-Output ("render diff: t500={0} t1200={1} t2500={2}" -f `
    (DiffSampled 'C:\devs\tmp\pr-base.bmp' 'C:\devs\tmp\pr-t500.bmp'), `
    (DiffSampled 'C:\devs\tmp\pr-base.bmp' 'C:\devs\tmp\pr-t1200.bmp'), `
    (DiffSampled 'C:\devs\tmp\pr-base.bmp' 'C:\devs\tmp\pr-t2500.bmp'))
Remove-Item C:\devs\tmp\pr-*.bmp -ErrorAction SilentlyContinue
Remove-ItemProperty $root -Name NOC_GLOBAL_SETTING_TOASTS_ENABLED -ErrorAction SilentlyContinue
Remove-ItemProperty $root -Name NOC_GLOBAL_SETTING_DND -ErrorAction SilentlyContinue
'registry cleaned'
