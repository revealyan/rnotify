using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace rnotify.SpikeWpf;

/// <summary>
/// Джойстик сценарных прогонов: показывает/закрывает карточку, дёргает
/// клик-тест (фокус жертве), пишет лог событий (столы, фокус). Обычное окно —
/// НЕ topmost, в alt-tab виден (карточка — нет).
/// </summary>
public partial class JoystickWindow : Window
{
	private CardWindow? _card;
	private DesktopSwitchWatcher? _watcher;
	private nint _victimHwnd;

	public JoystickWindow()
	{
		InitializeComponent();
		Loaded += OnLoaded;
		Closed += OnClosed;
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// Столы (сценарий С8): пересоздание карточки по смене виртуального
		// стола — паттерн старого rnotif (канон §9), дебаунс 250 мс.
		_watcher = new DesktopSwitchWatcher();
		_watcher.DesktopSwitched += OnDesktopSwitched;
		LogLine("watcher: подписан (события стола — в лог)");
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		_watcher?.Dispose();
		_card?.Close();
		Application.Current.Shutdown();
	}

	// Событие приходит в потоке насоса хука — маршалит на Dispatcher.
	private void OnDesktopSwitched() => Dispatcher.BeginInvoke(RecreateCardForDesktop);

	private void RecreateCardForDesktop()
	{
		LogLine("desktop-switch: пересоздаю sticky-карточку (если висит)");
		if (_card is not null)
		{
			// Пересоздание вместо переноса: окно живёт в столе, где создано
			// (опыт старого rnotif — IVirtualDesktopManager мёртв на 26200).
			bool sticky = _card.AutoTtlSeconds is null;
			_card.Close();
			_card = null;
			if (sticky)
			{
				ShowCard(sticky: true);
			}
		}
	}

	private void OnShowCard(object sender, RoutedEventArgs e) => ShowCard(sticky: false);

	private void OnShowSticky(object sender, RoutedEventArgs e) => ShowCard(sticky: true);

	private void ShowCard(bool sticky)
	{
		_card?.Close();
		_card = new CardWindow { AutoTtlSeconds = sticky ? null : 5 };
		_card.FocusRequested += OnCardFocusRequested;
		_card.Show();
		LogLine($"card: показана ({(sticky ? "sticky" : "ttl 5s")})");
	}

	private void OnDismissCard(object sender, RoutedEventArgs e)
	{
		_card?.Dismiss();
	}

	// Сценарий С7: тело карточки кликнуто → поднять окно-жертву (блокнот).
	// ALT-трюк перед SetForegroundWindow — обход foreground-lock (опыт rhub).
	private void OnCardFocusRequested()
	{
		LogLine("card: клик по телу → фокус жертве");
		RaiseVictim();
	}

	private void OnFocusVictim(object sender, RoutedEventArgs e)
	{
		EnsureVictim();
		RaiseVictim();
	}

	private void EnsureVictim()
	{
		if (_victimHwnd != 0)
		{
			return;
		}

		Process[] notepads = Process.GetProcessesByName("notepad");
		if (notepads.Length == 0)
		{
			using Process? started = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
			_ = started?.WaitForInputIdle(3000);
			notepads = Process.GetProcessesByName("notepad");
		}

		foreach (Process p in notepads)
		{
			if (p.MainWindowHandle != 0)
			{
				_victimHwnd = p.MainWindowHandle;
				LogLine($"victim: блокнот hwnd 0x{_victimHwnd:X}");
				return;
			}
		}

		LogLine("victim: окно блокнота не найдено");
	}

	private void RaiseVictim()
	{
		EnsureVictim();
		if (_victimHwnd == 0)
		{
			return;
		}

		if (Native.IsIconic(_victimHwnd))
		{
			_ = Native.ShowWindow(_victimHwnd, 9 /* SW_RESTORE */);
		}

		Native.keybd_event(0x12 /* VK_MENU */, 0, 2 /* KEYEVENTF_KEYUP */, 0); // ALT-трюк
		bool ok = Native.SetForegroundWindow(_victimHwnd);
		LogLine($"focus: SetForegroundWindow → {ok}");
	}

	internal void LogLine(string line)
	{
		StringBuilder sb = new(Log.Text.Length + line.Length + 2);
		_ = sb.Append(Log.Text).AppendLine(line);
		Log.Text = sb.ToString();
		Debug.WriteLine($"spikewpf: {line}");
	}

	internal static class Native
	{
		[DllImport("user32.dll")]
		internal static extern bool SetForegroundWindow(nint hWnd);

		[DllImport("user32.dll")]
		internal static extern bool IsIconic(nint hWnd);

		[DllImport("user32.dll")]
		internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

		[DllImport("user32.dll")]
		internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
	}
}
