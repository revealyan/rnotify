# Проба состояния rnotify: процесс жив? окно видно? Э1 держит? (живой прогон S6.1)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class WProbe
{
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
}
'@

$p = Get-Process rnotify -ErrorAction SilentlyContinue
if ($null -eq $p) {
    'process DEAD'
    exit
}

"process ALIVE pid $($p.Id)"
if ($p.MainWindowHandle -ne 0) {
    "main window: handle $($p.MainWindowHandle), visible = " + [WProbe]::IsWindowVisible($p.MainWindowHandle)
} else {
    'main window: скрыто/нет (в трее)'
}

$root = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings'
$global = (Get-Item $root).GetValue('NOC_GLOBAL_SETTING_TOASTS_ENABLED')
"global NOC_GLOBAL = $global"
$marker = Join-Path $env:USERPROFILE '.rnotify\suppression.json'
"marker: $(Test-Path $marker)"
