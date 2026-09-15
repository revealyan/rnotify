using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace rnotify.Render;

/// <summary>
/// Плашка «Пропустить всё» над стеком догоняющих (S6.4, просьба владельца —
/// «как в телеге»): неактивируемая topmost-плашка в зоне тостов, клик —
/// единственное действие. Контракт: все вызовы на Dispatcher (как CardStack).
/// </summary>
public partial class CatchUpBanner : Window
{
	/// <summary>Клик «Пропустить всё» — слить очередь догоняющих в floor.</summary>
	internal event EventHandler? SkipRequested;

	/// <summary>Создаёт плашку с числом остатка очереди.</summary>
	internal CatchUpBanner(int remaining)
	{
		InitializeComponent();
		Loaded += OnLoaded;
		Update(remaining);
	}

	/// <summary>Обновляет счётчик остатка (каждая показанная карточка).</summary>
	internal void Update(int remaining)
	{
		CountText.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
			remaining == 1 ? Strings.CatchUpRemainingOne : Strings.CatchUpRemainingMany, remaining);
		SkipButton.Content = Strings.SkipCatchUp;
	}

	private void OnSkipClick(object sender, RoutedEventArgs e) => SkipRequested?.Invoke(this, EventArgs.Empty);

	// Та же механика зоны, что у карточки: правый-низ WorkArea выбранного экрана.
	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		Rect area = ScreenPicker.WorkArea();
		Left = area.Right - Width - 12;
		MoveAbove(); // фиксированное место над полной подкладкой (S8.1)
	}

	/// <summary>
	/// ФИКСИРОВАННОЕ место (правка живого прогона S8.1): над слотом тройки
	/// с запасом под полную подкладку — плашка не поднимается по мере роста
	/// и не ездит со стеком; подкладка растёт к ней снизу.
	/// </summary>
	internal void MoveAbove() => Top = ScreenPicker.WorkArea().Bottom - Height - 12
		- (124 + 2 * 140 + 24); // прижата к тройке; подкладка растёт ЗА ней (z: BringToFront)

	/// <summary>Наверх topmost-полосы: подложенные карточки создаются позже и садятся выше плашки (S8.1).</summary>
	internal void BringToFront()
	{
		nint hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
		_ = User32.SetWindowPos(hwnd, -1 /* HWND_TOPMOST: ре-вставка наверх topmost-банда */,
			0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOMOVE | NOSIZE | NOACTIVATE */);
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		// WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW — как карточка: не крадёт фокус,
		// не торчит в alt-tab.
		nint hwnd = new WindowInteropHelper(this).EnsureHandle();
		int style = User32.GetWindowLong(hwnd, -20 /* GWL_EXSTYLE */);
		_ = User32.SetWindowLong(hwnd, -20, style | 0x80000000 /* WS_EX_NOACTIVATE */ | 0x00000080 /* WS_EX_TOOLWINDOW */);
	}

	private static class User32
	{
		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
		internal static extern int GetWindowLong(nint hWnd, int index);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
		internal static extern int SetWindowLong(nint hWnd, int index, long value);
	}
}
