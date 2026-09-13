using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace rnotify.SpikeWpf;

/// <summary>
/// Карточка-оверлей: безрамочное topmost неактивируемое окно в зоне нативного
/// баннера (правый-низ рабочей области). Механика — прецедент старого rnotif
/// (канон §9): ShowActivated=false + WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW; вход
/// slide+fade 250 мс (кривая Fluent), выход fade 167 мс.
/// </summary>
public partial class CardWindow : Window
{
	private const int GwlExStyle = -20;
	private const int WsExNoActivate = 0x0800_0000;
	private const int WsExToolWindow = 0x0000_0080;

	/// <summary>Клик по телу карточки — «фокус отправителю» (сценарий С7).</summary>
	public event Action? FocusRequested;

	/// <summary>Секунды авто-закрытия; null — sticky (без таймера).</summary>
	public int? AutoTtlSeconds { get; init; }

	private DispatcherTimer? _ttlTimer;
	private bool _closing;

	public CardWindow()
	{
		InitializeComponent();
		Loaded += OnLoaded;
		PlaceAtToastZone();
	}

	// Позиция: правый-нижний угол рабочей области основного монитора, отступ 12
	// DIP — зона нативного баннера (уточняется раундом 0, гипотеза Г1).
	// Left/Top окна — в DIP: PMv2-манифест, WPF считает масштаб сам.
	private void PlaceAtToastZone()
	{
		Left = SystemParameters.WorkArea.Right - Width - 12;
		Top = SystemParameters.WorkArea.Bottom - Height - 12;
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		nint hwnd = new WindowInteropHelper(this).Handle;
		int style = NativeMethods.GetWindowLong(hwnd, GwlExStyle);
		_ = NativeMethods.SetWindowLong(hwnd, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// Вход: сдвиг вправо-вниз + прозрачность → ноль, 250 мс, CubicEase-out
		// (Fluent «Direct Entrance», MS Motion doc).
		Slide.X = 40;
		Slide.Y = 24;
		Opacity = 0;
		DoubleAnimation slideX = new(40, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase() };
		DoubleAnimation slideY = new(24, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase() };
		DoubleAnimation fade = new(0, 1, TimeSpan.FromMilliseconds(167));
		Slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideX);
		Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideY);
		BeginAnimation(OpacityProperty, fade);

		if (AutoTtlSeconds is { } ttl)
		{
			_ttlTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ttl) };
			_ttlTimer.Tick += (_, _) => Dismiss();
			_ttlTimer.Start();
		}
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
		DoubleAnimation fade = new(1, 0, TimeSpan.FromMilliseconds(167));
		Storyboard exit = new();
		exit.Children.Add(fade);
		Storyboard.SetTarget(fade, this);
		Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
		exit.Completed += (_, _) => Close();
		exit.Begin();
	}

	private void OnCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		// Сценарий С7: клик по телу — не активируя себя, просим поднять окно
		// отправителя (джойстик делает SetForegroundWindow + ALT-трюк).
		FocusRequested?.Invoke();
		Dismiss();
	}

	private void OnCloseClick(object sender, RoutedEventArgs e) => Dismiss();

	internal static class NativeMethods
	{
		[DllImport("user32.dll")]
		internal static extern int GetWindowLong(nint hWnd, int nIndex);

		[DllImport("user32.dll")]
		internal static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
	}
}
