using System.Runtime.InteropServices;
using System.Windows;

namespace rnotify.Render;

/// <summary>
/// Work area экрана для зоны карточек (S7.1 мультимонитор). Режим settings.json
/// cardScreen: "cursor" (где мышь — дефолт, как нативные тосты Win11) |
/// "primary" (главный, как до S7.1) | "active" (переднее окно). Один экран —
/// все режимы дают primary, поведение неизменно. Перевод: MonitorFromPoint/
/// MonitorFromWindow → GetMonitorInfoW (rcWork, физические пиксели) →
/// GetDpiForMonitor (effective) → DIP для WPF Left/Top. Смешанный DPI на
/// стыках экранов даёт микро-сдвиг — принять (заметность ниже цены точного
/// пересчёта в координатах каждого окна).
/// </summary>
internal static class ScreenPicker
{
	/// <summary>Режим из settings.json (MainWindow ставит при старте; null → курсор).</summary>
	internal static string? Mode { get; set; }

	/// <summary>Work area выбранного экрана в DIP.</summary>
	internal static Rect WorkArea() => Mode switch
	{
		"primary" => SystemParameters.WorkArea,
		"active" => WorkAreaOf(ForegroundMonitor()),
		_ => WorkAreaOf(CursorMonitor()), // cursor — дефолт
	};

	private static nint CursorMonitor()
	{
		_ = GetCursorPos(out POINT pt);
		return MonitorFromPoint(pt, _monitorDefaultToNearest);
	}

	private static nint ForegroundMonitor() => MonitorFromWindow(GetForegroundWindow(), _monitorDefaultToNearest);

	// rcWork — физические пиксели (PMv2-процесс); WPF-окна позиционируются в
	// DIP → делим на DPI выбранного монитора (шэл-эффективный, как у WPF).
	private static Rect WorkAreaOf(nint monitor)
	{
		MONITORINFO info = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
		if (monitor == 0 || !GetMonitorInfoW(monitor, ref info))
		{
			return SystemParameters.WorkArea; // нет монитора — безопасный primary
		}

		float scale = 1.0f;
		if (GetDpiForMonitor(monitor, 0 /* EFFECTIVE_DPI */, out uint dpiX, out _) == 0 && dpiX > 0)
		{
			scale = dpiX / 96f;
		}

		return new Rect(
			info.WorkArea.Left / scale,
			info.WorkArea.Top / scale,
			(info.WorkArea.Right - info.WorkArea.Left) / scale,
			(info.WorkArea.Bottom - info.WorkArea.Top) / scale);
	}

	private const uint _monitorDefaultToNearest = 2;

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint MonitorFromPoint(POINT pt, uint flags);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint MonitorFromWindow(nint hwnd, uint flags);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetCursorPos(out POINT pt);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint GetForegroundWindow();

	[DllImport("shcore.dll")]
	private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT(int x, int y)
	{
		public int X = x;
		public int Y = y;
	}

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
