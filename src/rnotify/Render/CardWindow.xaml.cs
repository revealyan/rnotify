using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using rnotify.Core.Listener;
using rnotify.Core.Rules;

namespace rnotify.Render;

/// <summary>
/// Карточка-оверлей (порт из спайка S4.1): безрамочное topmost неактивируемое
/// окно в зоне нативного баннера (правый-низ рабочей области, поля ~12 DIP,
/// эталон 372×110 DIP). Контент и поведение — из вердикта правил: TTL
/// (null = sticky), акцент-полоса группы, клик Close/Focus. Вход slide+fade
/// 250 мс (кривая Fluent), выход fade 167 мс.
/// </summary>
public partial class CardWindow : Window
{
	private const int _gwlExStyle = -20;
	private const int _wsExNoActivate = 0x0800_0000;
	private const int _wsExToolWindow = 0x0000_0080;

	/// <summary>Клик по телу карточки при вердикте click=focus (стек поднимает отправителя).</summary>
	internal event EventHandler? FocusRequested;

	internal uint NotificationId => _record.Id;

	/// <summary>Sticky (без TTL) — единственные, кого стек пересоздаёт по смене стола.</summary>
	internal bool IsSticky => _verdict.Ttl is null;

	private readonly NotificationRecord _record;
	private readonly RuleVerdict _verdict;
	private readonly int _offsetDip;
	private DispatcherTimer? _ttlTimer;
	private bool _closing;

	internal CardWindow(NotificationRecord record, RuleVerdict verdict, Color? accent, int offsetDip)
	{
		InitializeComponent();
		_record = record;
		_verdict = verdict;
		_offsetDip = offsetDip;

		SenderText.Text = SenderFromAumid(record.Aumid);
		TitleText.Text = record.Title;
		BodyText.Text = record.Body;
		if (accent is { } color)
		{
			AccentStrip.Background = new SolidColorBrush(color);
			AccentStrip.Visibility = Visibility.Visible;
		}

		Loaded += OnLoaded;
		PlaceAtToastZone();
	}

	/// <summary>
	/// Отображаемое имя отправителя из AUMID: хвост после последнего разделителя
	/// пути со срезом «.exe» (тосты нести полный путь вида
	/// {SID}\...\powershell.exe — канон §10c). DisplayName листенер не даёт
	/// (резолв имени/иконки — остаток Э2).
	/// </summary>
	internal static string SenderFromAumid(string? aumid)
	{
		if (string.IsNullOrWhiteSpace(aumid))
		{
			return "<без AUMID>";
		}

		int tail = Math.Max(aumid.LastIndexOf('\\'), aumid.LastIndexOf('/')) + 1;
		string name = aumid[tail..];
		if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			name = name[..^4];
		}

		return name.Length > 0 ? name : aumid;
	}

	// Позиция: правый-нижний угол рабочей области основного монитора, отступ
	// 12 DIP (эталон нативного баннера, спайк S4.1 раунд 0). Left/Top — в DIP:
	// PMv2-манифест, WPF считает масштаб сам. offsetDip — сдвиг вверх (стек).
	private void PlaceAtToastZone()
	{
		Left = SystemParameters.WorkArea.Right - Width - 12;
		Top = SystemParameters.WorkArea.Bottom - Height - 12 - _offsetDip;
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		nint hwnd = new WindowInteropHelper(this).Handle;
		int style = NativeMethods.GetWindowLong(hwnd, _gwlExStyle);
		_ = NativeMethods.SetWindowLong(hwnd, _gwlExStyle, style | _wsExNoActivate | _wsExToolWindow);
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// Вход: сдвиг вправо-вниз + прозрачность → ноль, 250 мс, CubicEase-out
		// (Fluent «Direct Entrance»; порт из спайка S4.1).
		Slide.X = 40;
		Slide.Y = 24;
		Opacity = 0;
		DoubleAnimation slideX = new(40, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase() };
		DoubleAnimation slideY = new(24, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase() };
		DoubleAnimation fade = new(0, 1, TimeSpan.FromMilliseconds(167));
		Slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideX);
		Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideY);
		BeginAnimation(OpacityProperty, fade);

		if (_verdict.Ttl is { } ttl)
		{
			_ttlTimer = new DispatcherTimer { Interval = ttl };
			_ttlTimer.Tick += (_, _) => Dismiss();
			_ttlTimer.Start();
		}
	}

	/// <summary>Мягкий выход: fade 167 мс (Fluent «Direct Exit»), затем Close.</summary>
	internal void Dismiss()
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

	protected override void OnClosed(EventArgs e)
	{
		// Активный DispatcherTimer корневится Dispatcher'ом и пережил бы Close,
		// тикая по закрытому окну (добавка против спайка).
		_ttlTimer?.Stop();
		base.OnClosed(e);
	}

	private void OnCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		// Клик не активирует карточку (WS_EX_NOACTIVATE): при click=focus просим
		// стек поднять окно отправителя, иначе — просто закрыть.
		if (_verdict.Click == ClickAction.Focus)
		{
			FocusRequested?.Invoke(this, EventArgs.Empty);
		}

		Dismiss();
	}

	private void OnCloseClick(object sender, RoutedEventArgs e) => Dismiss();

	private static class NativeMethods
	{
		[DllImport("user32.dll")]
		internal static extern int GetWindowLong(nint hWnd, int nIndex);

		[DllImport("user32.dll")]
		internal static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
	}
}
