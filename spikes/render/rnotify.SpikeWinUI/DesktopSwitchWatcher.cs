using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace rnotify.SpikeWinUI;

/// <summary>
/// Событие смены виртуального стола (WinUI 3-порт паттерна старого rnotif,
/// канон §9): SetWinEventHook(EVENT_SYSTEM_DESKTOP_SWITCH) на выделенном
/// потоке с message-pump; колбек пересылается на UI-Dispatcher подписчика.
/// Для AppWindow поведение — гипотеза Г4 (главное новое утверждение спайка).
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
	private readonly DispatcherQueue _dispatcher;
	private nint _hook;
	private uint _threadId;
	private bool _disposed;

	/// <summary>Смена стола (колбек уже перемаршален на UI-Dispatcher).</summary>
	public event Action? DesktopSwitched;

	public DesktopSwitchWatcher(DispatcherQueue dispatcher)
	{
		_dispatcher = dispatcher;
		_proc = OnWinEvent;
		_thread = new Thread(RunPump) { Name = "spike-winui-desktopwatch", IsBackground = true };
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

	// WinEvent-колбек в потоке насоса — на UI-поток подписчика.
	private void OnWinEvent(nint hook, uint @event, nint hwnd, int idObject, int idChild, uint thread, uint time)
	{
		_ = _dispatcher.TryEnqueue(() => DesktopSwitched?.Invoke());
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
