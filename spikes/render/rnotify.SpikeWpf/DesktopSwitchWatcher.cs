using System.Runtime.InteropServices;

namespace rnotify.SpikeWpf;

/// <summary>
/// Событие смены виртуального стола: SetWinEventHook(EVENT_SYSTEM_DESKTOP_
/// SWITCH) на выделенном потоке с message-pump (хук живёт, пока качается
/// насос). Паттерн старого rnotif (канон §9): coclass IVirtualDesktopManager
/// мёртв на 26200, свойство VirtualDesktopAdjustByZOrder игнорируется —
/// рабочий путь это событие + пересоздание окна. Дебаунс — на подписчике.
/// </summary>
internal sealed class DesktopSwitchWatcher : IDisposable
{
	private const uint EventSystemDesktopSwitch = 0x0020;
	private const uint WineventOutofcontext = 0x0000;

	private readonly Thread _thread;
	private readonly ManualResetEventSlim _started = new(false);
	// Ссылка на делегат обязательна: хук держит сырой указатель — без корня GC
	// соберёт делегат и нативный вызов упадёт.
	private readonly Native.WinEventDelegate _proc;
	private nint _hook;
	private uint _threadId;
	private bool _disposed;

	/// <summary>Смена стола (колбек приходит в потоке насоса — маршальте на UI).</summary>
	public event Action? DesktopSwitched;

	public DesktopSwitchWatcher()
	{
		_proc = OnWinEvent;
		_thread = new Thread(RunPump) { Name = "spike-wpf-desktopwatch", IsBackground = true };
		_thread.Start();
		_ = _started.Wait(3000);
	}

	// Хук требует message loop: поток качает GetMessage до остановки.
	private void RunPump()
	{
		_threadId = Native.GetCurrentThreadId();
		_hook = Native.SetWinEventHook(
			EventSystemDesktopSwitch, EventSystemDesktopSwitch,
			nint.Zero, _proc,
			0, 0, WineventOutofcontext);
		_started.Set();

		while (Native.GetMessage(out MSG msg, nint.Zero, 0, 0) > 0)
		{
			Native.TranslateMessage(ref msg);
			Native.DispatchMessage(ref msg);
		}
	}

	// WinEvent-колбек в потоке насоса — пересылаем подписчику как есть.
	private void OnWinEvent(nint hook, uint @event, nint hwnd, int idObject, int idChild, uint thread, uint time)
	{
		DesktopSwitched?.Invoke();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		if (_hook != 0)
		{
			_ = Native.UnhookWinEvent(_hook);
		}

		_ = Native.PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, nint.Zero, nint.Zero);
		_started.Dispose();
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MSG
	{
		public nint Hwnd;
		public uint Message;
		public nint WParam;
		public nint LParam;
		public uint Time;
		public int PtX;
		public int PtY;
	}

	private static class Native
	{
		public delegate void WinEventDelegate(
			nint hook, uint @event, nint hwnd, int idObject, int idChild, uint thread, uint time);

		[DllImport("user32.dll")]
		public static extern nint SetWinEventHook(
			uint eventMin, uint eventMax, nint mod, WinEventDelegate proc,
			uint idProcess, uint idThread, uint flags);

		[DllImport("user32.dll")]
		public static extern bool UnhookWinEvent(nint hook);

		[DllImport("user32.dll")]
		public static extern int GetMessage(out MSG msg, nint hwnd, uint min, uint max);

		[DllImport("user32.dll")]
		public static extern bool TranslateMessage(ref MSG msg);

		[DllImport("user32.dll")]
		public static extern nint DispatchMessage(ref MSG msg);

		[DllImport("user32.dll")]
		public static extern uint GetCurrentThreadId();

		[DllImport("user32.dll")]
		public static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);
	}
}
