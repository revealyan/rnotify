using System.Diagnostics;
using System.Runtime.InteropServices;

namespace rnotify.Render;

/// <summary>
/// Эвристика «клик по карточке → поднять окно отправителя» (вердикт click:
/// focus): имя процесса из AUMID → процессы с окнами → окно максимальной
/// площади → ALT-трюк + SetForegroundWindow (обход foreground-lock, опыт
/// rhub/старого rnotif). Нет матча — карточка просто закрывается.
/// Вызывается синхронно в клик-хендлере: GetProcessesByName — десятки мс,
/// для редкого клика приемлемо (прецедент спайка S4.1).
/// </summary>
internal static class VictimFocus
{
	/// <summary>Пытается поднять окно отправителя; detail — результат для трейса.</summary>
	internal static bool TryFocus(string? aumid, out string detail)
	{
		string name = CardWindow.SenderFromAumid(aumid);
		if (name.Length == 0 || name.StartsWith('<'))
		{
			detail = "нет AUMID — просто закрыть";
			return false;
		}

		nint best = nint.Zero;
		long bestArea = 0;
		string bestTitle = string.Empty;
		foreach (Process process in Process.GetProcessesByName(name))
		{
			using Process current = process;
			nint hwnd = current.MainWindowHandle;
			if (hwnd == 0)
			{
				continue;
			}

			if (!Native.GetWindowRect(hwnd, out Native.RECT rect))
			{
				continue;
			}

			long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
			if (area > bestArea)
			{
				best = hwnd;
				bestArea = area;
				bestTitle = current.MainWindowTitle;
			}
		}

		if (best == nint.Zero)
		{
			detail = $"процесс «{name}» без окон — просто закрыть";
			return false;
		}

		if (Native.IsIconic(best))
		{
			_ = Native.ShowWindow(best, 9 /* SW_RESTORE */);
		}

		// ALT-трюк: синтетическое «отпускание» ALT снимает foreground-lock,
		// после чего SetForegroundWindow проходит.
		Native.keybd_event(0x12 /* VK_MENU */, 0, 2 /* KEYEVENTF_KEYUP */, 0);
		bool raised = Native.SetForegroundWindow(best);
		detail = raised
			? $"поднято окно «{bestTitle}» ({bestArea} px²)"
			: $"SetForegroundWindow отказал для «{bestTitle}»";
		return raised;
	}

	private static class Native
	{
		[DllImport("user32.dll")]
		internal static extern bool SetForegroundWindow(nint hWnd);

		[DllImport("user32.dll")]
		internal static extern bool IsIconic(nint hWnd);

		[DllImport("user32.dll")]
		internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

		[DllImport("user32.dll")]
		internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

		[DllImport("user32.dll")]
		internal static extern bool GetWindowRect(nint hWnd, out RECT rect);

		[StructLayout(LayoutKind.Sequential)]
		internal struct RECT
		{
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}
	}
}
