# Проба конкретного hwnd: класс/титул/pid/visible (S6.1, разбор противоречия проб)
param([long]$Hwnd = 0)

Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class HProbe
{
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
}
'@

$p = Get-Process rnotify -ErrorAction SilentlyContinue
if ($null -eq $p) { 'process DEAD'; exit }
"pid = $($p.Id), MainWindowHandle = $($p.MainWindowHandle)"
$h = if ($Hwnd -ne 0) { [IntPtr]$Hwnd } else { $p.MainWindowHandle }
"проба hwnd $h"
"is window: " + [HProbe]::IsWindow($h)
"is visible: " + [HProbe]::IsWindowVisible($h)
[uint32]$owner = 0
[void][HProbe]::GetWindowThreadProcessId($h, [ref]$owner)
"owner pid: $owner"
$t = New-Object System.Text.StringBuilder 256
[void][HProbe]::GetWindowText($h, $t, 256)
"title: '$($t.ToString())'"
$c = New-Object System.Text.StringBuilder 256
[void][HProbe]::GetClassName($h, $c, 256)
"class: '$($c.ToString())'"
