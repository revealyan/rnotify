# Проба рендера rnotify.ico: LoadImage -> DrawIcon в битмап -> сколько непрозрачных
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class IcoRender
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr LoadImage(IntPtr h, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr h);
}
'@

$path = 'C:\devs\revealyan\rnotify\src\rnotify\Assets\rnotify.ico'
foreach ($size in @(16, 32, 48)) {
    $h = [IcoRender]::LoadImage([IntPtr]::Zero, $path, 1, $size, $size, 0x10)
    if ($h -eq [IntPtr]::Zero) { "${size}px: LoadImage FAIL"; continue }

    $icon = [System.Drawing.Icon]::FromHandle($h)
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawIcon($icon, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)))
    $g.Dispose()

    $opaque = 0
    for ($x = 0; $x -lt $size; $x++) {
        for ($y = 0; $y -lt $size; $y++) {
            if ($bmp.GetPixel($x, $y).A -gt 0) { $opaque++ }
        }
    }
    "${size}px: непрозрачных $opaque из $($size * $size)"
    $bmp.Dispose()
    $icon.Dispose()
    [void][IcoRender]::DestroyIcon($h)
}
