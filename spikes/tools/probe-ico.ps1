# Проба иконки трея в установленном пакете (S6.1): файл есть? LoadImage жив?
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class IcoTest
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr LoadImage(IntPtr h, string name, uint type, int cx, int cy, uint flags);
}
'@

$loc = (Get-AppxPackage revealyan.RNotify).InstallLocation
$ico = Join-Path $loc 'Assets\rnotify.ico'
"пакет: $loc"
if (Test-Path $ico) {
    "ico в пакете: $((Get-Item $ico).Length) bytes"
    $h = [IcoTest]::LoadImage([IntPtr]::Zero, $ico, 1, 0, 0, 0x10)
    if ($h -eq [IntPtr]::Zero) {
        "LoadImage: FAIL (LastError $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))"
    } else {
        "LoadImage: OK -> $h"
    }
} else {
    'ico В ПАКЕТЕ НЕТ'
}

# и рядом с бинарём (откуда читает приложение):
$bin = Join-Path $loc 'rnotify.exe'
"exe есть: $(Test-Path $bin)"
