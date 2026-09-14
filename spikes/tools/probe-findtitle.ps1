# Поиск top-level окна rnotify по титулу (S6.1: где панель «RNotify»?)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class FWProbe
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string cls, string title);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
}
'@

$h = [FWProbe]::FindWindow($null, 'RNotify')
"FindWindow('RNotify') -> $h"
if ($h -ne [IntPtr]::Zero) {
    'visible: ' + [FWProbe]::IsWindowVisible($h)
    [uint32]$owner = 0
    [void][FWProbe]::GetWindowThreadProcessId($h, [ref]$owner)
    "owner pid: $owner"
}
