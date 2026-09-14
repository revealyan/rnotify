using System.Runtime.InteropServices;
using System.Windows.Interop;
using IOPath = System.IO.Path;

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
	private const uint _menuAutostart = 101;
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

		string iconPath = IOPath.Combine(AppContext.BaseDirectory, "Assets", "rnotify.ico");
		_icon = LoadImage(IntPtr.Zero, iconPath, _imageIcon, 0, 0, _lrLoadFromFile);

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
		return tray;
	}

	private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
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
			_ = AppendMenuW(menu, _mfString, _menuPanel, "Панель");
			_ = AppendMenuW(menu, _mfString | (AutostartChecked ? _mfChecked : 0), _menuAutostart, "Автозапуск");
			_ = AppendMenuW(menu, _mfSeparator, 0, "");
			_ = AppendMenuW(menu, _mfString, _menuExit, "Выход");

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
				case _menuAutostart:
					AutostartToggled?.Invoke(this, EventArgs.Empty);
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

	// --- Win32 ---

	private const uint _nimAdd = 0x0;
	private const uint _nimDelete = 0x2;
	private const uint _nifMessage = 0x1;
	private const uint _nifIcon = 0x2;
	private const uint _nifTip = 0x4;
	private const uint _wmNull = 0x0;
	private const uint _wmLbuttonUp = 0x0202;
	private const uint _wmLbuttonDblclk = 0x0203;
	private const uint _wmRbuttonUp = 0x0205;
	private const uint _mfString = 0x0;
	private const uint _mfSeparator = 0x800;
	private const uint _mfChecked = 0x8;
	private const uint _tpmRightButton = 0x2;
	private const uint _tpmReturnCmd = 0x100;
	private const uint _imageIcon = 1;
	private const uint _lrLoadFromFile = 0x10;

	[DllImport("shell32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr LoadImage(IntPtr hInstance, [MarshalAs(UnmanagedType.LPWStr)] string name, uint type, int cx, int cy, uint load);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DestroyIcon(IntPtr hIcon);

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
