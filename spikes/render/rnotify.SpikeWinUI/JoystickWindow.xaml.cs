using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace rnotify.SpikeWinUI;

/// <summary>
/// Джойстик сценарных прогонов (WinUI 3): те же кнопки, что у WPF-спайка —
/// показ sticky/TTL-карточки, клик-тест (фокус жертве), лог событий (столы,
/// фокус). Заодно факт: второе окно WinUI 3 в одном процессе (зрелость
/// мульт-окон).
/// </summary>
public sealed partial class JoystickWindow : Window
{
	private CardWindow? _card;
	private DesktopSwitchWatcher? _watcher;
	private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
	private nint _victimHwnd;

	public JoystickWindow()
	{
		InitializeComponent();
		Activated += OnFirstActivated;
	}

	private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
	{
		Activated -= OnFirstActivated;
		// Window.Dispatcher — легаси CoreDispatcher; очередь WinUI берём сами.
		_dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
		// Столы (сценарий С8): паттерн старого rnotif — событие + пересоздание.
		_watcher = new DesktopSwitchWatcher(_dispatcherQueue!);
		_watcher.DesktopSwitched += OnDesktopSwitched;
		LogLine("watcher: подписан (события стола — в лог)");
	}

	private void OnDesktopSwitched() => _ = (_dispatcherQueue!.TryEnqueue(RecreateCardForDesktop));

	private void RecreateCardForDesktop()
	{
		LogLine("desktop-switch: пересоздаю sticky-карточку (если висит)");
		if (_card is not null)
		{
			// Пересоздание вместо переноса: окно живёт в столе, где создано
			// (опыт старого rnotif — IVirtualDesktopManager мёртв на 26200).
			bool sticky = _card.IsSticky;
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
		if (_card is not null)
		{
			_card.Close();
		}

		_card = new CardWindow(sticky ? null : 5) { IsSticky = sticky };
		_card.FocusRequested += OnCardFocusRequested;
		_card.ShowNoActivate();
		LogLine($"card: показана ({(sticky ? "sticky" : "ttl 5s")})");
	}

	private void OnDismissCard(object sender, RoutedEventArgs e) => _card?.Dismiss();

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
		Debug.WriteLine($"spikewinui: {line}");
		_ = (_dispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread())
			.TryEnqueue(() => Log.Text = $"{Log.Text}{line}\n");
	}

	internal static partial class Native
	{
		[System.Runtime.InteropServices.DllImport("user32.dll")]
		internal static extern bool SetForegroundWindow(nint hWnd);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		internal static extern bool IsIconic(nint hWnd);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
	}
}
