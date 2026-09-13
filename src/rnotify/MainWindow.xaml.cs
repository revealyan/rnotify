using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using Windows.ApplicationModel;
using rnotify.Core;
using rnotify.Core.Listener;

namespace rnotify;

/// <summary>
/// Живая проверка MSIX-identity (S5.1) и поток уведомлений листенера (Э2):
/// окно показывает Package.Current.Id, сверяет с <see cref="ProductIdentity"/>
/// и после загрузки запускает <see cref="NotificationFeed"/>, печатая события
/// в панель. Запуск вне пакета (F5) — так и пишет.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
	Justification = "Жизненный цикл _feed — окно: Dispose в OnClosed (см. прецедент App с мьютексом)")]
public partial class MainWindow : Window
{
	// Панель — до первого контента; фид живёт в окне (Dispose в OnClosed).
	private NotificationFeed? _feed;

	public MainWindow()
	{
		InitializeComponent();
		Loaded += OnLoaded;
	}

	// async void — WPF-обработчик (прецедент спайка S5.2); тело под try/catch.
	[SuppressMessage("Design", "CA1031:Do not catch general exception types",
		Justification = "Граница UI: ошибку запуска листенера показываем строкой в панели и живём дальше")]
	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		ShowIdentity();
		try
		{
			_feed = new NotificationFeed(new UserNotificationSource());
			_feed.Added += OnNotificationAdded;
			_feed.Removed += OnNotificationRemoved;
			_feed.SnapshotFailed += OnSnapshotFailed;

			// ConfigureAwait(true) явно: продолжение (строки в панель) обязано
			// вернуться на Dispatcher; в ядре — всегда false, здесь — намеренно UI.
			ListenerStartResult start = await _feed.StartAsync().ConfigureAwait(true);
			AddRow("Consent (Э2)", start.Status.ToString());
			AddRow("Baseline", $"{start.BaselineCount} уведомл. пропущено (backlog)");
		}
		catch (Exception ex)
		{
			AddRow("Листенер", $"ошибка запуска: {ex.Message}");
		}
	}

	protected override void OnClosed(EventArgs e)
	{
		// Отписка до смерти Dispatcher: опаздывающий BeginInvoke на погашенном
		// Dispatcher абортится молча.
		_feed?.Dispose();
		base.OnClosed(e);
	}

	// События фида приходят из пула потоков — маршалит на Dispatcher
	// (неблокирующе; discard — прецедент спайка S5.2).
	private void OnNotificationAdded(object? sender, NotificationAddedEventArgs e)
	{
		_ = Dispatcher.BeginInvoke(() => AddRow($"+ id {e.Record.Id}", Describe(e.Record)));
	}

	private void OnNotificationRemoved(object? sender, NotificationRemovedEventArgs e)
	{
		_ = Dispatcher.BeginInvoke(() => AddRow($"− id {e.Id}", string.Empty));
	}

	private void OnSnapshotFailed(object? sender, NotificationFailedEventArgs e)
	{
		_ = Dispatcher.BeginInvoke(() => AddRow("Снапшот", $"ошибка: {e.Error.Message}"));
	}

	// AUMID · Заголовок — Тело; контент не прочитан (гипотеза 22621) — помечаем.
	private static string Describe(NotificationRecord record)
	{
		string app = record.Aumid ?? "<без AUMID>";
		if (record.Title.Length == 0 && record.Body.Length == 0)
		{
			return $"{app} · контент не прочитан";
		}

		string text = record.Body.Length == 0
			? record.Title
			: $"{record.Title} — {record.Body}";
		return $"{app} · {Truncate(text, 80)}";
	}

	private static string Truncate(string text, int max)
	{
		const string ellipsis = "…";
		return text.Length <= max ? text : $"{text[..(max - ellipsis.Length)]}{ellipsis}";
	}

	private void ShowIdentity()
	{
		try
		{
			PackageId id = Package.Current.Id;
			AddRow("Режим", "packaged (MSIX)");
			AddRow("Name", id.Name);
			AddRow("Version", $"{id.Version.Major}.{id.Version.Minor}.{id.Version.Build}.{id.Version.Revision}");
			AddRow("Publisher", id.Publisher);
			AddRow("FamilyName", id.FamilyName);
			AddRow(
				"Совпадение с ProductIdentity",
				string.Equals(id.Name, ProductIdentity.Name, StringComparison.Ordinal)
				&& string.Equals(id.Publisher, ProductIdentity.Publisher, StringComparison.Ordinal)
					? "да"
					: "НЕТ — манифест и код разошлись");
		}
		catch (InvalidOperationException)
		{
			// Package.Current вне пакета (unpackage F5-запуск) кидает — это норма.
			AddRow("Режим", "не в пакете (unpackaged, F5)");
		}
	}

	private void AddRow(string key, string value)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
		row.Children.Add(new TextBlock { Text = key, Width = 180, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
		row.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
		IdentityPanel.Children.Add(row);
	}
}
