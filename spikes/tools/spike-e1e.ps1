# Spike E1-C: self-validating suppression matrix (fullscreen guard + C0 sanity gate)
$ErrorActionPreference = 'Continue'
Add-Type -MemberDefinition '
[DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
' -Name DpiFix -Namespace Native
[void][Native.DpiFix]::SetProcessDpiAwareness(2)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type '
using System;
using System.Runtime.InteropServices;
using System.Text;
public class FG {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
  [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MI i);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  public struct R { public int L, T, Rt, B; }
  public struct MI { public uint Cb; public R Mon; public R Work; public uint Flags; }
}'

function ForegroundInfo {
    $h = [FG]::GetForegroundWindow()
    if ($h -eq [IntPtr]::Zero) { return @{ Name = '(none)'; Full = $false } }
    $r = New-Object FG+R
    [void][FG]::GetWindowRect($h, [ref]$r)
    $mi = New-Object FG+MI
    $mi.Cb = [Runtime.InteropServices.Marshal]::SizeOf([type][FG+MI])
    $m = [FG]::MonitorFromWindow($h, 2)
    [void][FG]::GetMonitorInfo($m, [ref]$mi)
    $full = ($r.L -le $mi.Mon.L) -and ($r.T -le $mi.Mon.T) -and ($r.Rt -ge $mi.Mon.Rt) -and ($r.B -ge $mi.Mon.B)
    $p = 0
    [void][FG]::GetWindowThreadProcessId($h, [ref]$p)
    $name = '?'
    try { $name = (Get-Process -Id $p -ErrorAction Stop).ProcessName } catch {}
    return @{ Name = $name; Full = $full }
}

$b = @{ Width = [Native.DpiFix]::GetSystemMetrics(0); Height = [Native.DpiFix]::GetSystemMetrics(1) }
$w = 700; $h = 340   # физическая зона тостов (~460x220 логических при 150%)

function ShotPath([string]$tag) { return "C:\devs\tmp\fs-$tag.bmp" }

function Shot([string]$tag) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(($b.Width - $w), ($b.Height - $h - 90), 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $bmp.Save((ShotPath $tag), [System.Drawing.Imaging.ImageFormat]::Bmp)
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

$root = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings'
$aumid = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
$appkey = Join-Path $root $aumid

function ApplyConfig([string]$name) {
    Remove-ItemProperty $root -Name NOC_GLOBAL_SETTING_TOASTS_ENABLED -ErrorAction SilentlyContinue
    Remove-ItemProperty $root -Name NOC_GLOBAL_SETTING_DND -ErrorAction SilentlyContinue
    Remove-ItemProperty $appkey -Name ShowBanner -ErrorAction SilentlyContinue
    switch ($name) {
        'C0' {
            Set-ItemProperty $root -Name NOC_GLOBAL_SETTING_TOASTS_ENABLED -Type DWord -Value 1
            Set-ItemProperty $root -Name NOC_GLOBAL_SETTING_DND -Type DWord -Value 0
        }
        'C1' { Set-ItemProperty $root -Name NOC_GLOBAL_SETTING_TOASTS_ENABLED -Type DWord -Value 0 }
        'C2' {
            Set-ItemProperty $root -Name NOC_GLOBAL_SETTING_TOASTS_ENABLED -Type DWord -Value 0
            Set-ItemProperty $appkey -Name ShowBanner -Type DWord -Value 0
        }
        'C3' {
            Set-ItemProperty $root -Name NOC_GLOBAL_SETTING_TOASTS_ENABLED -Type DWord -Value 0
            Set-ItemProperty $root -Name NOC_GLOBAL_SETTING_DND -Type DWord -Value 1
        }
    }
}

function SendToast([string]$scenario) {
    [void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]
    [void][Windows.UI.Notifications.ToastNotification,Windows.UI.Notifications,ContentType=WindowsRuntime]
    [void][Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom,ContentType=WindowsRuntime]
    $xdoc = New-Object Windows.Data.Xml.Dom.XmlDocument
    $sc = ''
    $actions = ''
    if ($scenario -ne '') {
        $sc = " scenario=`"$scenario`""
        $actions = '<actions><action content="OK" activationType="protocol" arguments="https://example.org"/></actions>'
    }
    $xdoc.LoadXml("<toast$sc><visual><binding template=`"ToastGeneric`"><text>spike e1c</text><text>$scenario test</text></binding></visual>$actions</toast>")
    $t = New-Object Windows.UI.Notifications.ToastNotification($xdoc)
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($aumid).Show($t)
}


function Clear-Center {
    for ($i = 0; $i -lt 15; $i++) {
        $out = & 'C:/devs/revealyan/rnotif/src/rnotif.Dump/bin/Debug/net10.0-windows10.0.22621.0/rnotif.Dump.exe' --remove-latest 2>&1 | Out-String
        if ($out -match 'empty' -or $out -match 'center is empty') { break }
        Start-Sleep -Milliseconds 300
    }
}

function MeasureToast([string]$cfg, [string]$sc) {
    ApplyConfig $cfg
    Start-Sleep -Milliseconds 800
    Shot "$cfg-$sc-base1"
    Start-Sleep -Milliseconds 300
    Shot "$cfg-$sc-base2"
    $noise = DiffSampled (ShotPath "$cfg-$sc-base1") (ShotPath "$cfg-$sc-base2")
    SendToast $sc
    Start-Sleep -Milliseconds 400
    Shot "$cfg-$sc-t400"
    Start-Sleep -Milliseconds 600
    Shot "$cfg-$sc-t1000"
    Start-Sleep -Milliseconds 1500
    Shot "$cfg-$sc-t2500"
    $d1 = DiffSampled (ShotPath "$cfg-$sc-base2") (ShotPath "$cfg-$sc-t400")
    $d2 = DiffSampled (ShotPath "$cfg-$sc-base2") (ShotPath "$cfg-$sc-t1000")
    $d3 = DiffSampled (ShotPath "$cfg-$sc-base2") (ShotPath "$cfg-$sc-t2500")
    return [pscustomobject]@{ Config = $cfg; Scenario = $sc; Noise = $noise; D400 = $d1; D1000 = $d2; D2500 = $d3 }
}

# --- start gate: ждём, пока активное окно перестанет быть терминалом (до 120с) ---
Write-Output 'Waiting for clean foreground (switch to an EMPTY desktop, do not touch anything)...'
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    $fg = ForegroundInfo
    if ($fg.Name -notin @('WindowsTerminal','conhost','powershell','pwsh','cmd','claude')) { break }
    Start-Sleep -Seconds 2
}
Write-Output ("foreground ready: {0}" -f $fg.Name)

# --- pre-checks ---
$fg = ForegroundInfo
Write-Output ("foreground: {0}, fullscreen: {1}" -f $fg.Name, $fg.Full)
if ($fg.Full) {
    Write-Output "ABORT: foreground window is fullscreen - Windows suppresses banners over it."
    Write-Output "Switch to a desktop WITHOUT the fullscreen console (do not exit it), then rerun."
    exit 2
}

# sanity gate: C0 + plain toast MUST produce a big diff (banner visible)
$sanity = MeasureToast 'C0' 'sanity'
$sanity | Format-Table -AutoSize
$signal = @($sanity.D400, $sanity.D1000, $sanity.D2500) | Measure-Object -Maximum
if ($signal.Maximum -lt 80) {
    Write-Output ("ABORT: C0 sanity diff too low ({0}) - banners are suppressed by environment." -f $signal.Maximum)
    exit 3
}

Write-Output 'sanity OK - running matrix, freeze for ~50 seconds...'
$results = @()
foreach ($cfg in @('C0','C1','C2','C3')) {
    foreach ($sc in @('', 'reminder')) {
        if ($cfg -eq 'C0' -and $sc -eq '') { continue }  # уже измерено в sanity
        $results += MeasureToast $cfg $sc
        if ($sc -eq 'reminder') {
            & 'C:\devs
evealyan
notif\src
notif.Dumpin\Debug
et10.0-windows10.0.22621.0
notif.Dump.exe' --remove-latest | Out-Null
            Start-Sleep -Milliseconds 800
        }
    }
}
# финальная зачистка возможных хвостов
& 'C:\devs
evealyan
notif\src
notif.Dumpin\Debug
et10.0-windows10.0.22621.0
notif.Dump.exe' --remove-latest | Out-Null
ApplyConfig 'C0'
$results | Format-Table -AutoSize
Remove-Item C:\devs\tmp\fs-*.bmp -ErrorAction SilentlyContinue
'registry back to default C0'
