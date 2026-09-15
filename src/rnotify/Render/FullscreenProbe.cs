using System.Runtime.InteropServices;

namespace rnotify.Render;

/// <summary>
/// Проба «foreground-окно занимает весь экран» (S8.1, hideOnFullscreen):
/// игра/презентация безрамочным бордом. Эвристика: прямоугольник переднего
/// окна покрывает прямоугольник своего монитора (допуск 2 px). Локскрин
/// (LockApp, канон §10a) тоже закрывает экран — для СКРЫТИЯ карточек это
/// корректно (за локом карточки не нужны).
/// </summary>
internal static class FullscreenProbe
{
	/// <summary>Переднее окно во весь экран своего монитора?</summary>
	internal static bool IsForegroundFullscreen()
	{
		nint foreground = GetForegroundWindow();
		if (foreground == 0)
		{
			return false;
		}

		if (!GetWindowRect(foreground, out RECT window))
		{
			return false;
		}

		nint monitor = MonitorFromWindow(foreground, 2 /* MONITOR_DEFAULTTONEAREST */);
		MONITORINFO info = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
		if (monitor == 0 || !GetMonitorInfoW(monitor, ref info))
		{
			return false;
		}

		RECT bounds = info.Monitor;
		return window.Left <= bounds.Left + 2 && window.Top <= bounds.Top + 2
			&& window.Right >= bounds.Right - 2 && window.Bottom >= bounds.Bottom - 2;
	}

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetWindowRect(nint hWnd, out RECT rect);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint hwnd, uint flags);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT(int left, int top, int right, int bottom)
	{
		public int Left = left;
		public int Top = top;
		public int Right = right;
		public int Bottom = bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MONITORINFO
	{
		public uint cbSize;
		public RECT Monitor;
		public RECT WorkArea;
		public uint Flags;
	}
}
