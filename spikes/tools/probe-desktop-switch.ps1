# Пробник событий смены виртуального стола (EVENT_SYSTEM_DESKTOP_SWITCH,
# 0x0020): ставим WinEvent-хук с насосом PeekMessage, сами жмём Win+Ctrl+→
# и Win+Ctrl+← синтетическими клавишами, считаем колбеки. Отвечает на вопрос
# «события вообще приходят на этой машине» — без приложения rnotify.
# Запуск: powershell -File probe-desktop-switch.ps1

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class DeskProbe
{
    public delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventDelegate proc, uint pid, uint tid, uint flags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint ms, uint wakeMask);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    public static int Count;
    public static int FgCount; // контроль: EVENT_SYSTEM_FOREGROUND (0x0003) — жива ли механика вообще
    public static long FirstMs, LastMs;
    private static readonly long Start = Environment.TickCount;
    private static readonly WinEventDelegate Proc = OnEvent;
    private static readonly WinEventDelegate FgProc = OnFgEvent;

    private static void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        Count++;
        long now = Environment.TickCount - Start;
        if (Count == 1) FirstMs = now;
        LastMs = now;
    }

    private static void OnFgEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        FgCount++;
    }

    public static IntPtr Install()
    {
        return SetWinEventHook(0x0020, 0x0020, IntPtr.Zero, Proc, 0, 0, 0);
    }

    public static IntPtr InstallForeground()
    {
        return SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, FgProc, 0, 0, 0);
    }

    // Насос — один в один как в rnotify-вотчере: выделенный фоновый поток с
    // блокирующим GetMessage-циклом (он и диспетчит WinEvent-колбеки). MsgWait+
    // GetMessage в главном потоке не годятся: QS_SENDMESSAGE будит ожидание,
    // а GetMessage после него блокируется навечно (ловушка, поймана живьём).
    public static void StartPump()
    {
        var pump = new Thread(() =>
        {
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
            }
        })
        { IsBackground = true };
        pump.Start();
    }

    public static void Pump(int seconds)
    {
        Thread.Sleep(seconds * 1000); // колбеки капают в фоновом насосе
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);

    // Свидетельство переключения: класс переднего окна до/после (на новом
    // столу foreground становится окно этого стола, обычно Explorer/WorkerW).
    public static string ForegroundDesc()
    {
        IntPtr hwnd = GetForegroundWindow();
        var sb = new StringBuilder(64);
        GetClassName(hwnd, sb, 64);
        return string.Format("0x{0:X}:{1}", hwnd.ToInt64(), sb);
    }

    // Win+Ctrl+стрелка: стрелку шлём сканкодом (KEYEVENTF_SCANCODE) + extended
    // (стрелки — расширенные клавиши: без 0xE0-префикса скан 0x4D читается как
    // numpad-6 и Explorer горячую клавишу не видит); модификаторам хватит VK.
    public static void PressWinCtrl(ushort vk, byte scan)
    {
        const uint keyUp = 0x0002, scanFlag = 0x0008, extended = 0x0001;
        keybd_event(0x11, 0, 0, UIntPtr.Zero);                              // Ctrl
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);                              // LWin
        keybd_event((byte)vk, scan, scanFlag | extended, UIntPtr.Zero);     // стрелка
        keybd_event((byte)vk, scan, scanFlag | extended | keyUp, UIntPtr.Zero);
        keybd_event(0x5B, 0, keyUp, UIntPtr.Zero);
        keybd_event(0x11, 0, keyUp, UIntPtr.Zero);
    }
}
'@

# Первая строка — integrity level процесса (UAC-фильтр или полный токен):
# True = админ-токен (high IL), False = обычный (medium IL).
$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Output ("probe: elevated={0}" -f $elevated)

$hook = [DeskProbe]::Install()
$fgHook = [DeskProbe]::InstallForeground()
if ($hook -eq [IntPtr]::Zero -or $fgHook -eq [IntPtr]::Zero) {
    Write-Output 'probe: SetWinEventHook FAILED (0)'
    exit 1
}
Write-Output ("probe: hook={0}, fg-hook={1}, foreground={2}" -f $hook, $fgHook, [DeskProbe]::ForegroundDesc())
[DeskProbe]::StartPump()
[DeskProbe]::Pump(1)

Write-Output 'probe: Zhmu Win+Ctrl+Right...'
[DeskProbe]::PressWinCtrl(0x27, 0x4D)
[DeskProbe]::Pump(3)
Write-Output ("probe: posle Right foreground={0}" -f [DeskProbe]::ForegroundDesc())

Write-Output 'probe: Zhmu Win+Ctrl+Left (vozvrat)...'
[DeskProbe]::PressWinCtrl(0x25, 0x4B)
[DeskProbe]::Pump(3)
Write-Output ("probe: posle Left foreground={0}" -f [DeskProbe]::ForegroundDesc())

[void][DeskProbe]::UnhookWinEvent($fgHook)
[void][DeskProbe]::UnhookWinEvent($hook)
Write-Output ("probe: desktop-switch(0x20)={0}, foreground(0x3)={1}" -f [DeskProbe]::Count, [DeskProbe]::FgCount)
if ([DeskProbe]::Count -gt 0) {
    Write-Output ("probe: pervoe +{0}ms, poslednee +{1}ms" -f [DeskProbe]::FirstMs, [DeskProbe]::LastMs)
}
