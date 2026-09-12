$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Windows.Forms
Add-Type '
using System;
using System.Runtime.InteropServices;
public class Dpi {
  [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
}'
Write-Output ("before: Screen={0}x{1}, GetSystemMetrics(0/1)={2}/{3}" -f `
    [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width,
    [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Height,
    [Dpi]::GetSystemMetrics(0), [Dpi]::GetSystemMetrics(1))
[void][Dpi]::SetProcessDpiAwareness(2)
Write-Output ("after:  Screen={0}x{1}, GetSystemMetrics(0/1)={2}/{3}" -f `
    [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width,
    [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Height,
    [Dpi]::GetSystemMetrics(0), [Dpi]::GetSystemMetrics(1))
