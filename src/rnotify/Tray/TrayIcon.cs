using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace rnotify.Tray;

/// <summary>
/// Иконка в области уведомлений: Shell_NotifyIcon на чистом P/Invoke (доктрина
/// нуля зависимостей; прецеденты P/Invoke — Render/). Callback-сообщения
/// ловит скрытое окно HwndSource (HWND живёт на Dispatcher — обработка в
/// UI-потоке; окно НЕ message-only: SetForegroundWindow для меню такой не
/// работает). Контекстное меню — Win32 TrackPopupMenu с _tpmReturnCmd и
/// танцем SetForegroundWindow + _wmNull (без него меню не закрывается кликом
/// мимо). Контракт: создавать и звать только на Dispatcher; Dispose — тоже.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
	private const uint _callbackMessage = 0x8000 + 0x49; // WM_APP + 0x49
	private const uint _idTray = 1;
	private const uint _menuPanel = 100;
	private const uint _menuHistory = 104;
	private const uint _menuRules = 105;
	private const int _hotkeyId = 1;
	private const int _rulesHotkeyId = 2;
	private const uint _menuAutostart = 101;
	private const uint _menuSkipCatchUp = 103;
	private const uint _menuExit = 102;

	private HwndSource? _source;
	private IntPtr _icon;
	private bool _added;
	private bool _disposed;

	/// <summary>Левый клик — открыть/поднять окно-панель.</summary>
	internal event EventHandler? PanelRequested;

	/// <summary>Меню «Выход» — настоящий выход (вернуть Э1, погасить процесс).</summary>
	internal event EventHandler? ExitRequested;

	/// <summary>Меню «Автозапуск» — переключить (S6.1/2: settings.json + StartupTask).</summary>
	internal event EventHandler? AutostartToggled;

	/// <summary>Меню «Пропустить догоняющие» — слить очередь догонялок в floor молча (S6.4).</summary>
	internal event EventHandler? SkipCatchUpRequested;

	/// <summary>Хоткей истории сработал (S7.2) — открыть/поднять панель истории.</summary>
	internal event EventHandler? HistoryHotkeyPressed;

	/// <summary>Меню «История…» — открыть панель истории.</summary>
	internal event EventHandler? HistoryMenuRequested;

	/// <summary>Хоткей редактора правил (S7.3).</summary>
	internal event EventHandler? RulesHotkeyPressed;

	/// <summary>Меню «Правила…» — открыть редактор правил.</summary>
	internal event EventHandler? RulesMenuRequested;

	/// <summary>Галочка «Автозапуск» на момент открытия меню (коммит 2).</summary>
	internal bool AutostartChecked { get; set; }

	/// <summary>Показать иконку в трее (иконка — Assets/rnotify.ico рядом с exe).</summary>
	internal void Show()
	{
		if (_source is null)
		{
			throw new InvalidOperationException("Трей не создан: зовите TrayIcon.Create() перед Show().");
		}

		if (_added)
		{
			return;
		}

		_icon = LoadIconResource();
		NOTIFYICONDATAW data = InitData();
		data.hWnd = _source.Handle;
		data.uFlags = _nifMessage | _nifIcon | _nifTip;
		data.uCallbackMessage = _callbackMessage;
		data.hIcon = _icon;
		data.szTip = "RNotify";
		if (!Shell_NotifyIconW(_nimAdd, ref data))
		{
			DestroyIcon(_icon);
			_icon = IntPtr.Zero;
			throw new InvalidOperationException("Shell_NotifyIcon(_nimAdd) не удался (трей переполнен?).");
		}

		_added = true;
	}

	/// <summary>Хоткей редактора правил — тот же механизм, другой id.</summary>
	internal bool TryRegisterRulesHotkey(string? spec)
	{
		if (_source is null || !TryParseHotkey(spec, out uint modifiers, out uint vk))
		{
			return false;
		}

		return RegisterHotKey(_source.Handle, _rulesHotkeyId, modifiers, vk);
	}

	/// <summary>
	/// Зарегистрировать глобальный хоткей вида "Win+Shift+N" на окно трея
	/// (WM_HOTKEY придёт в WndProc). false — комбинация занята/не разобрана
	/// (вызывающий скажет строкой; путь без хоткея — меню трея).
	/// </summary>
	internal bool TryRegisterHotkey(string? spec)
	{
		if (_source is null || !TryParseHotkey(spec, out uint modifiers, out uint vk))
		{
			return false;
		}

		return RegisterHotKey(_source.Handle, _hotkeyId, modifiers, vk);
	}

	// "Win+Shift+N" → MOD-набор + виртуальная клавиша; мусор — false.
	private static bool TryParseHotkey(string? spec, out uint modifiers, out uint vk)
	{
		modifiers = 0;
		vk = 0;
		if (string.IsNullOrWhiteSpace(spec))
		{
			return false;
		}

		string[] parts = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length < 2)
		{
			return false;
		}

		foreach (string part in parts[..^1])
		{
			switch (part.ToUpperInvariant())
			{
				case "WIN": modifiers |= 0x8; break;
				case "CTRL":
				case "CONTROL": modifiers |= 0x2; break;
				case "ALT": modifiers |= 0x1; break;
				case "SHIFT": modifiers |= 0x4; break;
				default: return false;
			}
		}

		string key = parts[^1].ToUpperInvariant();
		vk = key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z' ? key[0]
			: key.Length == 1 && key[0] >= '0' && key[0] <= '9' ? key[0]
			: key.StartsWith('F') && int.TryParse(key[1..], System.Globalization.CultureInfo.InvariantCulture, out int fn) && fn is >= 1 and <= 12 ? (uint)(0x70 + fn - 1)
			: 0;
		return vk != 0;
	}

	/// <summary>Создаёт иконку с окном-приёмником сообщений.</summary>
	internal static TrayIcon Create()
	{
		HwndSourceParameters parameters = new("rnotify-tray", 0, 0)
		{
			PositionX = -32000, // за экраном, окно никогда не показываем (но оно НЕ message-only:
			PositionY = -32000, // SetForegroundWindow для меню такого не работает)
		};
		HwndSource source = new(parameters);
		TrayIcon tray = new() { _source = source };
		source.AddHook(tray.WndProc);
		// HwndSource создаёт окно ВИДИМЫМ (0×0 за экраном): Get-Process считает его
		// «главным окном» процесса и оно торчит в z-порядке/alt-tab — гасим явно.
		_ = ShowWindow(source.Handle, _swHide);
		return tray;
	}

	private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		// taskkill и подобное шлют WM_CLOSE всем top-level окнам процесса: окно
		// трея закрываться не имеет права (HwndSource по умолчанию уничтожится,
		// иконка осиротеет) — глотаем, демон управляется только через меню.
		if (msg == _wmClose)
		{
			handled = true;
			return IntPtr.Zero;
		}

		if (msg == _wmHotkey)
		{
			if (wParam.ToInt32() == _hotkeyId)
			{
				HistoryHotkeyPressed?.Invoke(this, EventArgs.Empty);
			}
			else if (wParam.ToInt32() == _rulesHotkeyId)
			{
				RulesHotkeyPressed?.Invoke(this, EventArgs.Empty);
			}

			handled = true;
			return IntPtr.Zero;
		}

		if (msg == _callbackMessage)
		{
			// lParam — мышиное сообщение; клики по нетактивируемой зоне трея.
			uint mouse = unchecked((uint)lParam.ToInt64());
			if (mouse == _wmLbuttonUp || mouse == _wmLbuttonDblclk)
			{
				PanelRequested?.Invoke(this, EventArgs.Empty);
			}
			else if (mouse == _wmRbuttonUp)
			{
				ShowMenu();
			}

			handled = true;
			return IntPtr.Zero;
		}

		return IntPtr.Zero;
	}

	private void ShowMenu()
	{
		if (_source is null)
		{
			return;
		}

		IntPtr menu = CreatePopupMenu();
		try
		{
			_ = AppendMenuW(menu, _mfString, _menuPanel, Strings.TrayMenuPanel);
			_ = AppendMenuW(menu, _mfString, _menuHistory, Strings.TrayMenuHistory);
			_ = AppendMenuW(menu, _mfString, _menuRules, Strings.TrayMenuRules);
			_ = AppendMenuW(menu, _mfString | (AutostartChecked ? _mfChecked : 0), _menuAutostart, Strings.TrayMenuAutostart);
			_ = AppendMenuW(menu, _mfSeparator, 0, "");
			_ = AppendMenuW(menu, _mfString, _menuSkipCatchUp, Strings.SkipCatchUp);
			_ = AppendMenuW(menu, _mfString, _menuExit, Strings.TrayMenuExit);

			_ = GetCursorPos(out POINT pt);
			// Танец: без SetForegroundWindow меню не закрывается кликом мимо;
			// _wmNull после TrackPopupMenu закрывает окно из фокуса меню.
			_ = SetForegroundWindow(_source.Handle);
			uint choice = TrackPopupMenu(menu, _tpmRightButton | _tpmReturnCmd, pt.X, pt.Y, 0, _source.Handle, IntPtr.Zero);
			_ = PostMessageW(_source.Handle, _wmNull, IntPtr.Zero, IntPtr.Zero);

			switch (choice)
			{
				case _menuPanel:
					PanelRequested?.Invoke(this, EventArgs.Empty);
					break;
				case _menuHistory:
					HistoryMenuRequested?.Invoke(this, EventArgs.Empty);
					break;
				case _menuRules:
					RulesMenuRequested?.Invoke(this, EventArgs.Empty);
					break;
				case _menuAutostart:
					AutostartToggled?.Invoke(this, EventArgs.Empty);
					break;
				case _menuSkipCatchUp:
					SkipCatchUpRequested?.Invoke(this, EventArgs.Empty);
					break;
				case _menuExit:
					ExitRequested?.Invoke(this, EventArgs.Empty);
					break;
					// 0 — закрыли мимо: ничего
			}
		}
		finally
		{
			_ = DestroyMenu(menu);
		}
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		if (_added)
		{
			NOTIFYICONDATAW data = InitData();
			data.hWnd = _source?.Handle ?? IntPtr.Zero;
			data.uID = _idTray;
			_ = Shell_NotifyIconW(_nimDelete, ref data);
			_added = false;
		}

		if (_source is not null)
		{
			_ = UnregisterHotKey(_source.Handle, _hotkeyId);
			_ = UnregisterHotKey(_source.Handle, _rulesHotkeyId);
		}

		if (_icon != IntPtr.Zero)
		{
			DestroyIcon(_icon);
			_icon = IntPtr.Zero;
		}

		if (_source is not null)
		{
			_source.RemoveHook(WndProc);
			_source.Dispose();
			_source = null;
		}
	}

	private static NOTIFYICONDATAW InitData() => new() { cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(), uID = _idTray };

	// HICON из встроенного .ico (pack URI): LoadImage по файлу в WindowsApps
	// ловит ERROR_FILE_NOT_FOUND (грабля S6.1). Формат RT_ICON = кадр
	// BITMAPINFOHEADER+пиксели+маска — разбираем ICONDIR сами, кадр берём
	// ближайший к маленькой метрике иконок (трей ~SM_CXSMICON).
	private static IntPtr LoadIconResource()
	{
		using System.IO.Stream? stream = System.Windows.Application.GetResourceStream(
			new Uri("pack://application:,,,/Assets/rnotify.ico"))?.Stream;
		if (stream is null)
		{
			throw new InvalidOperationException("ресурс Assets/rnotify.ico не найден в сборке");
		}

		using System.IO.BinaryReader reader = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
		_ = reader.ReadUInt16(); // reserved
		_ = reader.ReadUInt16(); // type: 1 = icon
		ushort count = reader.ReadUInt16();
		if (count == 0)
		{
			throw new InvalidOperationException("ico без кадров");
		}

		int desired = GetSystemMetrics(_smCxsmIcon);
		int bestOffset = 0;
		int bestSize = 0;
		int bestDelta = int.MaxValue;
		for (int i = 0; i < count; i++)
		{
			int width = reader.ReadByte();
			_ = reader.ReadByte();   // height
			_ = reader.ReadByte();   // colors
			_ = reader.ReadByte();   // reserved
			_ = reader.ReadUInt16(); // planes
			_ = reader.ReadUInt16(); // bit count
			int bytes = reader.ReadInt32();
			int offset = reader.ReadInt32();
			int size = width == 0 ? 256 : width;
			int delta = Math.Abs(size - desired);
			if (delta < bestDelta)
			{
				bestDelta = delta;
				bestOffset = offset;
				bestSize = bytes;
			}
		}

		stream.Position = bestOffset;
		byte[] frame = new byte[bestSize];
		stream.ReadExactly(frame);
		IntPtr icon = CreateIconFromResourceEx(frame, (uint)frame.Length, fIcon: true, 0x00030000, 0, 0, _lrDefaultColor);
		return icon == IntPtr.Zero
			? throw new InvalidOperationException($"CreateIconFromResourceEx не дал HICON (err {Marshal.GetLastWin32Error()})")
			: icon;
	}

	// --- Win32 ---

	private const uint _nimAdd = 0x0;
	private const uint _nimDelete = 0x2;
	private const uint _nifMessage = 0x1;
	private const uint _nifIcon = 0x2;
	private const uint _nifTip = 0x4;
	private const uint _wmNull = 0x0;
	private const uint _wmClose = 0x0010;
	private const uint _wmHotkey = 0x0312;
	private const uint _wmLbuttonUp = 0x0202;
	private const uint _wmLbuttonDblclk = 0x0203;
	private const uint _wmRbuttonUp = 0x0205;
	private const uint _mfString = 0x0;
	private const uint _mfSeparator = 0x800;
	private const uint _mfChecked = 0x8;
	private const uint _tpmRightButton = 0x2;
	private const uint _tpmReturnCmd = 0x100;
	private const int _swHide = 0x0;
	private const int _smCxsmIcon = 49;
	private const uint _lrDefaultColor = 0x0;

	[DllImport("shell32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool fIcon, uint version, int cx, int cy, uint flags);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int GetSystemMetrics(int index);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DestroyIcon(IntPtr hIcon);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr CreatePopupMenu();

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DestroyMenu(IntPtr hMenu);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, [MarshalAs(UnmanagedType.LPWStr)] string lpNewItem);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetCursorPos(out POINT lpPoint);

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT(int x, int y)
	{
		public int X = x;
		public int Y = y;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct NOTIFYICONDATAW
	{
		public uint cbSize;
		public IntPtr hWnd;
		public uint uID;
		public uint uFlags;
		public uint uCallbackMessage;
		public IntPtr hIcon;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string szTip;
		public uint dwState;
		public uint dwStateMask;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string szInfo;
		public uint uVersion;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		public string szInfoTitle;
		public uint dwInfoFlags;
		public Guid guidItem;
		public IntPtr hBalloonIcon;
	}
}
