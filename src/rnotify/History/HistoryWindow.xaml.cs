using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using rnotify.Core.History;

namespace rnotify.History;

/// <summary>
/// Панель истории (S7.2): снимок HistoryStore при открытии (+кнопка обновить),
/// поиск по отправителю/титулу/телу, фильтр «только показанные» (дефолт —
/// настройка historyOnlyShown). Действия над записью — расширяемый реестр
/// HistoryActions (v1: повтор карточкой-липучкой, копировать текст). Esc —
/// закрыть. Не модальная: можно держать открытой, поток карточек живёт.
/// </summary>
public partial class HistoryWindow : Window
{
	private readonly Func<IReadOnlyList<HistoryEntry>> _load;
	private readonly Action<HistoryEntry> _reshow;

	// CompositeFormat-кэш (CA1863): счётчик «N из K» на каждый Refresh.
	private static readonly global::System.Text.CompositeFormat _countFormat =
		global::System.Text.CompositeFormat.Parse(Strings.HistoryCountFormat);

	private HistoryWindow(Func<IReadOnlyList<HistoryEntry>> load, bool onlyShownDefault, Action<HistoryEntry> reshow)
	{
		_load = load;
		_reshow = reshow;
		InitializeComponent();
		Deactivated += (_, _) => Hide(); // пропал фокус — спрятать (просьба владельца, живой прогон S7.2)
		Title = Strings.HistoryTitle;
		OnlyShownCheck.Content = Strings.HistoryOnlyShown;
		RefreshButton.Content = Strings.HistoryRefresh;
		SearchBox.Tag = Strings.HistorySearchHint;
		OnlyShownCheck.IsChecked = onlyShownDefault;
		Refresh();
	}

	/// <summary>Открыть/поднять панель (одна на приложение — по хоткею не плодим).</summary>
	internal static void ShowSingle(
		Func<IReadOnlyList<HistoryEntry>> load, bool onlyShownDefault, Action<HistoryEntry> reshow)
	{
		HistoryWindow? existing = Application.Current.Windows.OfType<HistoryWindow>().FirstOrDefault();
		if (existing is not null)
		{
			existing.Refresh();
			existing.Show();
			existing.Activate();
			return;
		}

		new HistoryWindow(load, onlyShownDefault, reshow).Show();
	}

	// Строка списка: плоская ViewModel над HistoryEntry + кнопки-действия.
	public sealed record RowViewModel(string Time, string Sender, string Title, string Action, HistoryEntry Entry, IReadOnlyList<HistoryAction> Actions);

	/// <summary>Кнопка-действие (расширяемый реестр — v1 повтор/копия).</summary>
	public sealed record HistoryAction(string Glyph, string Hint, Action<HistoryEntry> Run);

	private void Refresh()
	{
		IReadOnlyList<HistoryEntry> entries = _load();
		Items.ItemsSource = Filtered(entries).Select(ToRow).ToArray();
		CountText.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, _countFormat, Items.Items.Count, entries.Count);
	}

	private IEnumerable<HistoryEntry> Filtered(IReadOnlyList<HistoryEntry> entries)
	{
		bool onlyShown = OnlyShownCheck.IsChecked == true;
		string needle = SearchBox.Text.Trim();
		foreach (HistoryEntry entry in entries)
		{
			if (onlyShown && !string.Equals(entry.Action, "show", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (needle.Length > 0
				&& !Contains(entry.SenderName) && !Contains(entry.Title) && !Contains(entry.Body))
			{
				continue;
			}

			yield return entry;

			bool Contains(string? field) =>
				field is not null && field.Contains(needle, StringComparison.OrdinalIgnoreCase);
		}
	}

	private RowViewModel ToRow(HistoryEntry entry) => new(
		Time: DateTimeOffset.FromUnixTimeSeconds(entry.RaisedUnix).LocalDateTime.ToString("dd.MM HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
		Sender: entry.SenderName ?? entry.Aumid ?? "<без AUMID>",
		Title: entry.Title,
		Action: entry.Action,
		Entry: entry,
		Actions:
		[
			new HistoryAction("↺", Strings.HistoryActionReshow, e => _reshow(e)),
			new HistoryAction("⧉", Strings.HistoryActionCopy, CopyEntry),
		]);

	private void CopyEntry(HistoryEntry entry)
	{
		string text = string.Join(Environment.NewLine,
		[
			$"{DateTimeOffset.FromUnixTimeSeconds(entry.RaisedUnix).LocalDateTime:yyyy-MM-dd HH:mm:ss}",
			$"{Strings.HistoryCopySender}: {entry.SenderName ?? entry.Aumid}",
			$"{Strings.HistoryCopyTitle}: {entry.Title}",
			$"{Strings.HistoryCopyBody}: {entry.Body}",
		]);
		try
		{
			Clipboard.SetText(text);
		}
		catch (System.Runtime.InteropServices.ExternalException)
		{
			// Клипборд занят чужим процессом — тихо (панель живёт).
		}
	}

	private void OnFilterChanged(object sender, RoutedEventArgs e) => Refresh();

	private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

	private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (((ListView)sender).SelectedItem is RowViewModel row)
		{
			_reshow(row.Entry);
		}
	}

	private void OnActionClick(object sender, RoutedEventArgs e)
	{
		// DataContext кнопки — сама HistoryAction (ItemsControl ячейки); строка
		// списка — выше по визуальному дереву (грабля живого прогона S7.2:
		// кнопки «молчали», дубль-клик через ListView работал).
		if (sender is not Button { Tag: HistoryAction action } button)
		{
			return;
		}

		DependencyObject? node = button;
		while (node is not null && node is not ListViewItem)
		{
			node = System.Windows.Media.VisualTreeHelper.GetParent(node);
		}

		if (node is ListViewItem { DataContext: RowViewModel row })
		{
			action.Run(row.Entry);
		}
	}

	private void OnWindowKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			Close();
		}
	}
}
