using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using Windows.ApplicationModel;
using rnotify.Core;
using rnotify.Core.Listener;
using rnotify.Core.Rules;
using rnotify.Render;

namespace rnotify;

/// <summary>
/// Живая проверка MSIX-identity (S5.1), поток уведомлений листенера (Э2),
/// вердикты правил (Э3) и рендер карточек (Э4): окно показывает
/// Package.Current.Id, события фида — с вердиктом; show-вердикт открывает
/// карточку стека, delete/killNative сносят нативную копию из Центра живьём,
/// чужой Removed гасит карточку. Запуск вне пакета (F5) — так и пишет.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
	Justification = "Жизненный цикл _feed/_stack/_rulesMonitor — окно: Dispose в OnClosed (см. прецедент App с мьютексом)")]
public partial class MainWindow : Window
{
	// Панель — до первого контента; фид, стек карточек и монитор правил живут в
	// окне (Dispose в OnClosed). Строки дублируются списком: кнопка «Скопировать
	// всё» отдаёт их клипбордом (канал верификации для владельца — вместо
	// скриншотов).
	private NotificationFeed? _feed;
	private UserNotificationSource? _source;
	private RulesReloader? _rules;
	private RulesMonitor? _rulesMonitor;
	private CardStack? _stack;
	// Анти-самоснос (канон §10c): killNative = показать свою карточку И снести
	// нативную копию — собственный Removed-дифф не должен гасить эту карточку.
	private readonly Lock _selfRemovedGate = new();
	private HashSet<uint> _selfRemoved = [];
	private readonly List<(string Key, string Value)> _rows = [];

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
			StartRules();

			// Стек — до старта фида: события подписываются раньше StartAsync,
			// первый Added не должен прийти без стека. Трейс стека — всегда на
			// Dispatcher (контракт CardStack), AddRow напрямую.
			_stack = new CardStack();
			_stack.Trace += OnStackTrace;

			_source = new UserNotificationSource();
			_feed = new NotificationFeed(_source);
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

	// Правила грузятся ДО старта фида: первый же тост получает вердикт.
	private void StartRules()
	{
		RulesStore store = new();
		_rules = new RulesReloader(store);
		if (_rules.StartupError is { } error)
		{
			AddRow("Правила", $"ошибка: {error.Message} — работаем на дефолте");
		}
		else
		{
			AddRow("Правила", $"{store.FilePath}: {DescribeRules(_rules.Current)}");
		}

		_rules.Reloaded += OnRulesReloaded;
		_rulesMonitor = new RulesMonitor(store, _rules);
	}

	protected override void OnClosed(EventArgs e)
	{
		// Стек — первым (карточки и вотчер стола), затем монитор и отписка:
		// не перезагрузиться в момент разборки; опаздывающий BeginInvoke на
		// погашенном Dispatcher абортится молча.
		_stack?.Dispose();
		_rulesMonitor?.Dispose();
		if (_rules is not null)
		{
			_rules.Reloaded -= OnRulesReloaded;
		}

		_feed?.Dispose();
		base.OnClosed(e);
	}

	// События фида приходят из пула потоков — маршалит на Dispatcher
	// (неблокирующе; discard — прецедент спайка S5.2).
	private void OnNotificationAdded(object? sender, NotificationAddedEventArgs e)
	{
		// Вердикт — один раз, здесь: движок immutable, потокобезопасен.
		RuleVerdict verdict = _rules?.Current.Decide(e.Record) ?? RuleVerdict.CatchAll;
		uint id = e.Record.Id;
		string text = $"{Describe(e.Record)} ▸ {FormatVerdict(verdict)}";

		// BeginInvoke ДО Remove: очередь Dispatcher FIFO, «+» всегда выше «−»;
		// карточка show-вердикта открывается той же посылкой (стек — контракт
		// Dispatcher-only, e.Record/verdict замкнуты, движок immutable).
		_ = Dispatcher.BeginInvoke(() =>
		{
			AddRow($"+ id {id}", text);
			if (verdict.Action == RuleAction.Show)
			{
				_stack?.Show(e.Record, verdict);
			}
		});
		ApplyNativeRemoval(verdict, id);
	}

	private void OnNotificationRemoved(object? sender, NotificationRemovedEventArgs e)
	{
		// «− id» и гашение карточки — только для ЧУЖОГО сноса (юзер из Центра,
		// вытеснение хранилища); собственный killNative/delete-снос отфильтрован
		// (Remove=true = id был в сетке наших сносов), иначе гасил бы только что
		// показанную карточку.
		bool wasSelfRemoved;
		lock (_selfRemovedGate)
		{
			wasSelfRemoved = _selfRemoved.Remove(e.Id);
		}

		if (wasSelfRemoved)
		{
			return;
		}

		_ = Dispatcher.BeginInvoke(() =>
		{
			AddRow($"− id {e.Id}", string.Empty);
			_stack?.CloseById(e.Id);
		});
	}

	// Трейс стека карточек (контракт: событие всегда на Dispatcher).
	private void OnStackTrace(object? sender, CardTraceEventArgs e)
	{
		AddRow("Стек", e.Message);
	}

	private void OnSnapshotFailed(object? sender, NotificationFailedEventArgs e)
	{
		_ = Dispatcher.BeginInvoke(() => AddRow("Снапшот", $"ошибка: {e.Error.Message}"));
	}

	// Хот-релоад: движок уже подменён релоадером, окну остаётся сказать вслух.
	private void OnRulesReloaded(object? sender, RulesReloadedEventArgs e)
	{
		_ = Dispatcher.BeginInvoke(() => AddRow(
			"Правила",
			e.Success
				? $"перезагружено: {DescribeRules(e.Engine)}"
				: $"ошибка: {e.Error!.Message} — работает прежний конфиг"));
	}

	// delete/killNative сносят нативную копию из Центра — управление хранилищем
	// по правилу пользователя, не «показать и прихлопнуть» (конституция п.2).
	// Id помечается ДО вызова (intent-first): собственный Removed-дифф прилетает
	// из пула и может обогнать возврат RemoveNotification — без пометки нарушили
	// бы порядок «+» выше «−» и погасили killNative-карточку её же сносом.
	[SuppressMessage("Design", "CA1031:Do not catch general exception types",
		Justification = "WinRT-HRESULT на уже исчезнувшем Id не должен ронять поток событий фида (хендлер бежит в цикле диффа)")]
	private void ApplyNativeRemoval(RuleVerdict verdict, uint id)
	{
		if (!verdict.RequiresNativeRemoval)
		{
			return;
		}

		lock (_selfRemovedGate)
		{
			_ = _selfRemoved.Add(id);
		}

		try
		{
			_source?.RemoveNotification(id);
		}
		catch (Exception ex)
		{
			lock (_selfRemovedGate)
			{
				_ = _selfRemoved.Remove(id);
			}

			_ = Dispatcher.BeginInvoke(() => AddRow($"! id {id}", $"снос из Центра не удался: {ex.Message}"));
		}
	}

	// «N правил, выброшено M: имена (причины)» — отчёт выбраковки видим, не молча.
	private static string DescribeRules(RulesEngine engine)
	{
		string discarded = engine.Discarded.Count == 0
			? string.Empty
			: $", выброшено {engine.Discarded.Count}: {string.Join("; ", engine.Discarded.Select(DescribeDiscard))}";
		return $"{engine.RuleCount} правил{discarded}";
	}

	private static string DescribeDiscard(DiscardedRule rule) =>
		$"{rule.RuleName ?? "<без имени>"} ({rule.Reason})";

	// «Группа/правило → mute · killNative · ttl 3m»; catch-all — «— (без правила)».
	private static string FormatVerdict(RuleVerdict verdict)
	{
		if (verdict.GroupName is null && verdict.RuleName is null)
		{
			return "— (без правила)";
		}

		List<string> notes = [];
		if (verdict.KillNative)
		{
			notes.Add("killNative");
		}

		if (verdict.Ttl is { } ttl && ttl != RulesEngine.DefaultShowTtl)
		{
			notes.Add($"ttl {FormatDuration(ttl)}");
		}

		string action = verdict.Action.ToString().ToLowerInvariant();
		return notes.Count == 0
			? $"{verdict.GroupName}/{verdict.RuleName} → {action}"
			: $"{verdict.GroupName}/{verdict.RuleName} → {action} · {string.Join(" · ", notes)}";
	}

	private static string FormatDuration(TimeSpan ttl) => ttl.TotalMinutes >= 1
		? $"{ttl.TotalMinutes:0.#}m"
		: $"{ttl.TotalSeconds:0.#}s";

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

	// Копирует все строки панели в клипборд одной пачкой (владелец вставляет их
	// в сессию Claude). Буфер может быть занят чужим процессом — ExternalException.
	private void OnCopyClick(object sender, RoutedEventArgs e)
	{
		CopyButton.Content = "Скопировать всё";
		try
		{
			Clipboard.SetText(string.Join(Environment.NewLine, _rows.Select(r => $"{r.Key} = {r.Value}")));
			CopyButton.Content = $"Скопировано ({_rows.Count})";
		}
		catch (System.Runtime.InteropServices.ExternalException ex)
		{
			CopyButton.Content = $"Не вышло: {ex.Message}";
		}
	}

	private void AddRow(string key, string value)
	{
		_rows.Add((key, value));
		var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
		row.Children.Add(new TextBlock { Text = key, Width = 180, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
		row.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
		IdentityPanel.Children.Add(row);
	}
}
