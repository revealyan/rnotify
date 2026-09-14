# Проба всех видимых/невидимых top-level окон rnotify (S6.1)
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class WEnum
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
}
'@

$target = (Get-Process rnotify -ErrorAction SilentlyContinue).Id
if ($null -eq $target) { 'process DEAD'; exit }
"pid = $target"

$cb = [WEnum+EnumProc]{
    param($hWnd, $lParam)
    [uint32]$owner = 0
    [void][WEnum]::GetWindowThreadProcessId($hWnd, [ref]$owner)
    if ($owner -eq $lParam.ToInt64()) {
        $t = New-Object System.Text.StringBuilder 256
        [void][WEnum]::GetWindowText($hWnd, $t, 256)
        $c = New-Object System.Text.StringBuilder 256
        [void][WEnum]::GetClassName($hWnd, $c, 256)
        $vis = [WEnum]::IsWindowVisible($hWnd)
        "hwnd=$hWnd visible=$vis class='$($c.ToString())' title='$($t.ToString())'"
    }
    return $true
}
[void][WEnum]::EnumWindows($cb, [IntPtr]$target)
