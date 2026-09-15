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

	// Та же механика зоны, что у карточки: правый-низ WorkArea.
	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		Left = SystemParameters.WorkArea.Right - Width - 12;
		MoveAbove(3); // стартовая позиция — под полную тройку, дальше ездит со стеком
	}

	/// <summary>Ездит со стеком: прижата к верхней карточке (её верх − зазор 16).</summary>
	internal void MoveAbove(int stackCount) => Top = SystemParameters.WorkArea.Bottom - Height - 12
		- (124 + Math.Max(0, stackCount - 1) * 140 + 16);

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
		[DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
		internal static extern int GetWindowLong(nint hWnd, int index);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
		internal static extern int SetWindowLong(nint hWnd, int index, long value);
	}
}
