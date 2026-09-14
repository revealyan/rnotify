# Прицельный WM_CLOSE главному окну rnotify (проверка OnClosing-в-трей, S6.1)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class WClose
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

$p = Get-Process rnotify -ErrorAction SilentlyContinue
if ($null -eq $p) { 'process DEAD'; exit }
$h = $p.MainWindowHandle
if ($h -eq 0) { 'нет главного окна (уже скрыто?)'; exit }
"post WM_CLOSE -> $h"
[void][WClose]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Milliseconds 1500
$p2 = Get-Process rnotify -ErrorAction SilentlyContinue
if ($null -eq $p2) { 'process DEAD после WM_CLOSE (вышел — плохо!)'; exit }
"process ALIVE pid $($p2.Id)"
"MainWindowHandle теперь: $($p2.MainWindowHandle)"
if ($p2.MainWindowHandle -ne 0) {
    # handle может быть кэширован — проверяем видимость живого hwnd из старого значения
    'главное окно ещё числится — смотрим видимым ли было до кэша'
}
