using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WinRT.Interop;

namespace rnotify.SpikeWinUI;

/// <summary>
/// Карточка-оверлей WinUI 3: AppWindow + OverlappedPresenter (IsAlwaysOnTop,
/// без рамки и титлбара), DesktopAcrylicBackdrop, показ без активации —
/// AppWindow.Show(false) плюс WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW через interop
/// (гипотезы Г3/Г4). Позиция/размер — в физических пикселях (DisplayArea),
/// DIP-размер пересчитывается по DPI окна.
/// </summary>
public sealed partial class CardWindow : Window
{
	private const int GwlExStyle = -20;
	private const int WsExNoActivate = 0x0800_0000;
	private const int WsExToolWindow = 0x0000_0080;

	/// <summary>Клик по телу карточки — «фокус отправителю» (сценарий С7).</summary>
	public event Action? FocusRequested;

	/// <summary>Sticky-режим (для пересоздания по смене стола). Не init-only:
	/// генератор XamlTypeInfo не умеет присваивать init-свойства.</summary>
	public bool IsSticky { get; set; }

	private readonly DispatcherTimer? _ttlTimer;
	private bool _closing;
	private readonly bool _placeAfterShow;

	public CardWindow(int? autoTtlSeconds, bool addNoActivateStyle = true, bool addEntrance = true, bool addBackdrop = true, bool addPresenter = true, bool placeAtZone = true, int offsetDip = 0)
	{
		// Позиция — в ctor, ДО загрузки контента (HWND уже создан базовым
		// ctor). Координаты — физические (DPI-aware проверками DWM; пробы
		// без DPI-aware дают виртуализированные значения ÷scale — не верить).
		if (placeAtZone)
		{
			RectInt32 wa = DisplayArea.Primary.WorkArea;
			uint dpi = Native.GetDpiForWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
			double s = dpi / 96.0;
			int w = (int)Math.Round(372 * s);
			int h = (int)Math.Round(124 * s);
			int m = (int)Math.Round(12 * s);
			int dy = (int)Math.Round(offsetDip * s);
			AppWindow.MoveAndResize(new RectInt32(wa.X + wa.Width - w - m, wa.Y + wa.Height - h - m - dy, w, h));
		}

		InitializeComponent();

		if (addPresenter)
		{
			OverlappedPresenter presenter = (OverlappedPresenter)AppWindow.Presenter;
			presenter.IsAlwaysOnTop = true;
			presenter.IsResizable = false;
			presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
		}

		nint hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
		if (addNoActivateStyle)
		{
			int style = Native.GetWindowLong(hwnd, GwlExStyle);
			_ = Native.SetWindowLong(hwnd, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
		}

		// Материал: системный акрил на безрамочном окне (гипотеза Г3).
		if (addBackdrop && DesktopAcrylicController.IsSupported())
		{
			SystemBackdrop = new DesktopAcrylicBackdrop();
		}

		// Бисекция: тематический вход мог застрять на opacity 0.
		if (!addEntrance)
		{
			Root.Transitions.Clear();
		}

		// ВАЖНО (факт WASDK 2.4): MoveAndResize ДО AppWindow.Show() оставляет
		// окно без контента навсегда (DWM-границы есть, пикселей нет).
		// Позиционируем строго ПОСЛЕ показа — возможен кадр в каскадной точке.
		_placeAfterShow = placeAtZone;

		// Таймер стартует после ShowNoActivate (без активации окна).
		if (autoTtlSeconds is { } ttl)
		{
			_ttlTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ttl) };
			_ttlTimer.Tick += (_, _) => Dismiss();
		}
	}

	/// <summary>
	/// Показ без активации: в WASDK 2.x у AppWindow.Show нет параметра activate —
	/// не-активацию обеспечивает WS_EX_NOACTIVATE (установлен в ctor). Само
	/// отсутствие параметра — уже факт протокола (веб-доки 1.x устарели).
	/// Позиционирование — после Show (см. комментарий в ctor).
	/// </summary>
	public void ShowNoActivate()
	{
		AppWindow.Show();

		// Факт WASDK 2.4: presenter.IsAlwaysOnTop из ctor не применяется, а
		// внешний SetWindowPos(HWND_TOPMOST) молча откатывается — презентер
		// энфорсит свой z-порядок. Единственное, что сработало: повторная
		// установка свойства ПОСЛЕ показа окна.
		if (_placeAfterShow)
		{
			OverlappedPresenter presenter = (OverlappedPresenter)AppWindow.Presenter;
			presenter.IsAlwaysOnTop = true;
		}

		_ttlTimer?.Start();
	}

	// Зона нативного баннера: правый-низ рабочей области основного монитора,
	// отступ 12 DIP (гипотеза Г1, раунд 0). Факты WASDK 2.4: AppWindow.
	// MoveAndResize ставит геометрию верно, но контент окна умирает; SetWindowPos
	// контент сохраняет, но XAML-слой WinUI делит его аргументы на DPI-скейл —
	// эмпирика: чтобы попасть в физическую цель T, подаём T*scale (хак для
	// одного монитора; мультимонитор — S4.2).
	private void PlaceAtToastZone(nint hwnd)
	{
		double scale = Native.GetDpiForWindow(hwnd) / 96.0;
		int width = (int)Math.Round(372 * scale);
		int height = (int)Math.Round(124 * scale);
		int margin = (int)Math.Round(12 * scale);
		RectInt32 workArea = DisplayArea.Primary.WorkArea;
		int x = workArea.X + workArea.Width - width - margin;
		int y = workArea.Y + workArea.Height - height - margin;
		_ = Native.SetWindowPos(
			hwnd, Native.HwndTopmost,
			(int)Math.Round(x * scale), (int)Math.Round(y * scale),
			(int)Math.Round(width * scale), (int)Math.Round(height * scale),
			Native.SwpNoActivate | Native.SwpShowWindow);
	}

	/// <summary>Мягкий выход: fade 167 мс (Fluent «Direct Exit»), затем Close.</summary>
	public void Dismiss()
	{
		if (_closing)
		{
			return;
		}

		_closing = true;
		_ttlTimer?.Stop();
		DoubleAnimation fade = new()
		{
			From = 1,
			To = 0,
			Duration = new Duration(TimeSpan.FromMilliseconds(167)),
		};
		Storyboard exit = new();
		exit.Children.Add(fade);
		Storyboard.SetTarget(fade, Root);
		Storyboard.SetTargetProperty(fade, "Opacity");
		exit.Completed += (_, _) => Close();
		exit.Begin();
	}

	private void OnCardTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
	{
		FocusRequested?.Invoke();
		Dismiss();
	}

	private void OnCloseClick(object sender, RoutedEventArgs e) => Dismiss();

	internal static partial class Native
	{
		[System.Runtime.InteropServices.DllImport("user32.dll")]
		internal static extern int GetWindowLong(nint hWnd, int nIndex);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		internal static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		internal static extern uint GetDpiForWindow(nint hWnd);

		// Позиционирование карточки без AppWindow.MoveAndResize (см. PlaceAtToastZone).
		[System.Runtime.InteropServices.DllImport("user32.dll")]
		internal static extern bool SetWindowPos(
			nint hWnd, nint hWndInsertAfter,
			int x, int y, int width, int height, uint flags);

		internal static readonly nint HwndTopmost = new(-1);
		internal const uint SwpNoActivate = 0x0010;
		internal const uint SwpShowWindow = 0x0040;
		internal const uint SwpNoMove = 0x0002;
		internal const uint SwpNoSize = 0x0001;
	}
}
