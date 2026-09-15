using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.ApplicationModel;
using rnotify.Core;
using rnotify.Core.Listener;
using rnotify.Core.Rules;
using rnotify.Core.History;
using rnotify.Core.Settings;
using rnotify.History;
using rnotify.Core.Suppression;
using rnotify.Render;
using rnotify.Tray;
using StartupTaskState = Windows.ApplicationModel.StartupTaskState;

namespace rnotify;

/// <summary>
/// Живая проверка MSIX-identity (S5.1), поток уведомлений листенера (Э2),
/// вердикты правил (Э3), рендер карточек (Э4) и подавление нативных баннеров
/// формулой Э1 (S1.2): окно показывает Package.Current.Id, события фида —
/// с вердиктом; show-вердикт открывает карточку стека, delete/killNative
/// сносят нативную копию из Центра живьём, чужой Removed гасит карточку;
/// после consent фида применяется формула Э1 (глобальный тумблер + blanket
/// ShowBanner=0), на выходе — возврат прежних значений. S6.1: закрытие окна
/// сворачивает в трей (демон жив, Э1 держится) — настоящий выход из меню
/// трея. Запуск вне пакета (F5) — так и пишет.
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
	private NativeBannerSuppressor? _suppressor;
	private TrayIcon? _tray;
	private NotificationFloor? _floor;
	private HistoryStore? _history;
	private readonly Queue<NotificationRecord> _catchUpQueue = [];
	private System.Windows.Threading.DispatcherTimer? _catchUpPacer;
	private CatchUpBanner? _catchUpBanner;
	private AppSettingsStore? _settingsStore;
	private AppSettings _settings = new();
	// S6.1: закрытие окна = свернуть в трей; настоящий выход (меню трея
	// «Выход») ставит флаг и доезжает до разборки OnClosed.
	private bool _exiting;
	// Анти-самоснос (канон §10c): killNative = показать свою карточку И снести
	// нативную копию — собственный Removed-дифф не должен гасить эту карточку.
	private readonly Lock _selfRemovedGate = new();
	private HashSet<uint> _selfRemoved = [];
	private readonly List<(string Key, string Value)> _rows = [];
	// CompositeFormat-кэш (CA1863): смена языка — перезапуском, формат статичен.
	private static readonly global::System.Text.CompositeFormat _copiedFormat =
		global::System.Text.CompositeFormat.Parse(Strings.CopiedFormat);

	public MainWindow()
	{
		InitializeComponent();
		Loaded += OnLoaded;
		TrySetWindowIcon();
	}

	// Иконка окна — тем же встроенным Assets/rnotify.ico, что и трей (pack URI:
	// файл в WindowsApps для LoadImage-пути недоступен, грабля S6.1); сбой
	// ресурса не должен ронять окно.
	private void TrySetWindowIcon()
	{
		try
		{
			Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/rnotify.ico"));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or UriFormatException)
		{
			// Окно без иконки живо; трей скажет своей ошибкой, если ресурса нет совсем.
		}
	}

	// async void — WPF-обработчик (прецедент спайка S5.2); тело под try/catch.
	[SuppressMessage("Design", "CA1031:Do not catch general exception types",
		Justification = "Граница UI: ошибку запуска листенера показываем строкой в панели и живём дальше")]
	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		ShowIdentity();
		LoadSettings();
		StartTray();
		try
		{
			StartRules();

			// Floor «что уже обработано» — до фида: догонялка после старта
			// сверяет backlog по нему (S6.4). История — туда же (S7.2).
			_floor = new NotificationFloor();
			_history = new HistoryStore(_settings.HistoryLimit);

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

			StartSuppression(start.Status);
			CatchUpMissed(start.Baseline);
		}
		catch (Exception ex)
		{
			AddRow(Strings.RowListener, $"ошибка запуска: {ex.Message}");
		}
	}

	// Э1 (S1.2): формула подавления — после consent-решения фида. Allowed →
	// гасим; иначе — по настройке suppressWithoutListener (settings.json, дефолт
	// false — безопасно: без листенера карточек нет, молчаливое гашение оставило
	// бы пользователя вовсе без уведомлений). Первый Added нового отправителя
	// добивает blanket (OnNotificationAdded → BlanketSender). Не применена —
	// приложение живёт на нативных баннерах, строка в панель.
	[SuppressMessage("Design", "CA1031:Do not catch general exception types",
		Justification = "Граница UI: сбой применения формулы показываем строкой и живём на нативных баннерах")]
	private void StartSuppression(NotificationAccessStatus consent)
	{
		// Супрессор — всегда (S6.4): починка маркера краха обязана случиться и
		// без применения формулы, иначе баннеры юзера останутся погашенными.
		_suppressor = new NativeBannerSuppressor(new RegistryNotificationSettings());
		_suppressor.Trace += OnSuppressionTrace;

		if (consent != NotificationAccessStatus.Allowed && !_settings.SuppressWithoutListener)
		{
			AddRow("Э1", $"consent {consent} — нативные баннеры не гасим");
			try
			{
				_suppressor.TryRepairMarker(); // без применения: вернуть юзеру баннеры
			}
			catch (Exception ex)
			{
				AddRow("Э1", $"починка маркера не удалась: {ex.Message} — маркер оставлен, следующий старт попробует снова");
			}

			return;
		}

		try
		{
			// ГОНКА (живой прогон S7.1): отдельный TryRepairMarker ДО Apply
			// на секунду включал баннеры (ремонт global=1), фид уже слушал —
			// тост в зазоре давал карточку И нативный баннер. Теперь ремонт
			// живёт ВНУТРИ Apply одним локом: зазор — микросекунды.
			_suppressor.Apply();
		}
		catch (Exception ex)
		{
			AddRow("Э1", $"не применена: {ex.Message} — нативные баннеры остаются");
		}
	}

	// Настройки — раньше трея: чекбокс «Автозапуск» берётся из settings.json
	// (зеркало чекбокса; системная истина — StartupTask, показывается строкой
	// при переключении).
	private void LoadSettings()
	{
		_settingsStore = new AppSettingsStore();
		AppSettingsLoadResult load = _settingsStore.LoadOrDefault();
		_settings = load.Settings;
		ScreenPicker.Mode = _settings.CardScreen; // экран зоны карточек (S7.1)
		string values = $"suppressWithoutListener={_settings.SuppressWithoutListener}, autostart={_settings.Autostart}"
			+ $", screen={_settings.CardScreen ?? "cursor"}";
		AddRow(Strings.RowSettings, load.Error is { } error ? $"ошибка: {error.Message} — дефолт {values}" : $"{_settingsStore.FilePath}: {values}");
	}

	// Трей — до всего живого: «Выход» обязан работать даже если фид/правила
	// упали на старте (демон без панели всё равно управляем).
	[SuppressMessage("Design", "CA1031:Do not catch general exception types",
		Justification = "Трей — жизненно важный орган демона, но его отказ не должен ронять панель: строка и живём")]
	private void StartTray()
	{
		try
		{
			_tray = TrayIcon.Create();
			_tray.PanelRequested += (_, _) =>
			{
				Show();
				Activate();
			};
			_tray.ExitRequested += OnTrayExit;
			_tray.AutostartToggled += OnTrayAutostart;
			_tray.SkipCatchUpRequested += OnTraySkipCatchUp;
			_tray.HistoryHotkeyPressed += (_, _) => OpenHistory();
			_tray.HistoryMenuRequested += (_, _) => OpenHistory();
			string hotkey = _settings.HistoryHotkey ?? "Win+Shift+N";
			AddRow("История", _tray.TryRegisterHotkey(hotkey)
				? $"{hotkey} + меню трея; глубина {_settings.HistoryLimit}"
				: $"хоткей {hotkey} не встал (занят?) — открывать из меню трея");
			_tray.AutostartChecked = _settings.Autostart;
			_tray.Show();
			AddRow(Strings.RowTray, "иконка в области уведомлений (закрытие окна = свернуть сюда)");
		}
		catch (Exception ex)
		{
			AddRow(Strings.RowTray, $"не встал: {ex.Message} — выход по закрытию окна невозможен, процесс жив");
		}
	}

	// Чекбокс «Автозапуск»: WinRT StartupTask + зеркало в settings.json;
	// итог — состояние системы (юзер/политика могли не дать включить).
	[SuppressMessage("Design", "CA1031:Do not catch general exception types",
		Justification = "Граница UI: сбой StartupTask (unpackaged F5, политика) — строка в панель, демону всё равно")]
	private async void OnTrayAutostart(object? sender, EventArgs e)
	{
		bool desired = !_settings.Autostart;
		try
		{
			StartupTaskState state = await AutostartManager.SetAsync(desired).ConfigureAwait(true);
			bool enabled = state == StartupTaskState.Enabled;
			_settings = _settings with { Autostart = enabled };
			_settingsStore?.Save(_settings);
			if (_tray is not null)
			{
				_tray.AutostartChecked = enabled;
			}

			AddRow(Strings.RowAutostart, enabled ? $"включён (state: {state})" : $"не включился (state: {state})");
		}
		catch (Exception ex)
		{
			AddRow(Strings.RowAutostart, $"не переключился: {ex.Message}");
		}
	}

	// Панель истории (S7.2): одна на приложение, хоткей/меню; повтор записи —
	// карточкой-липучкой напрямую в стек (без повторного вердикта правил и
	// без дублирования в историю).
	private void OpenHistory()
	{
		if (_history is null)
		{
			return;
		}

		HistoryWindow.ShowSingle(_history.SnapshotNewestFirst, _settings.HistoryOnlyShown, OnHistoryReshow);
	}

	private void OnHistoryReshow(HistoryEntry entry)
	{
		NotificationRecord record = new(
			0, entry.Aumid, entry.Title, entry.Body, DateTimeOffset.FromUnixTimeSeconds(entry.RaisedUnix));
		_stack?.Show(record, RuleVerdict.CatchAll with { Ttl = null }, SenderResolver.Resolve(entry.Aumid));
		AddRow("История", $"повтор: {Truncate($"{entry.Title} — {entry.Body}", 80)}");
	}

	// Меню трея «Выход»: единственный путь к настоящей разборке (OnClosing
	// без флага отменяет закрытие и прячет окно в трей).
	private void OnTrayExit(object? sender, EventArgs e)
	{
		_exiting = true;
		Close();
	}

	// Правила грузятся ДО старта фида: первый же тост получает вердикт.
	private void StartRules()
	{
		RulesStore store = new();
		_rules = new RulesReloader(store);
		if (_rules.StartupError is { } error)
		{
			AddRow(Strings.RowRules, $"ошибка: {error.Message} — работаем на дефолте");
		}
		else
		{
			AddRow(Strings.RowRules, $"{store.FilePath}: {DescribeRules(_rules.Current)}");
		}

		_rules.Reloaded += OnRulesReloaded;
		_rulesMonitor = new RulesMonitor(store, _rules);
	}

	// S6.1: закрытие окна (X) = свернуть в трей — демон жив, Э1 держится,
	// разборка (ниже в OnClosed) не запускается. Настоящий выход ставит
	// _exiting (меню трея «Выход») и доезжает до OnClosed.
	protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
	{
		if (!_exiting)
		{
			e.Cancel = true;
			Hide();
			return;
		}

		base.OnClosing(e);
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types",
		Justification = "Граница выхода: неудача возврата формулы Э1 не должна ронять закрытие — маркер остаётся, следующий старт чинит")]
	protected override void OnClosed(EventArgs e)
	{
		// Догоняющий поток — прежде всего живого.
		_catchUpPacer?.Stop();
		if (_stack is not null)
		{
			_stack.SlotFreed -= OnCatchUpSlotFreed;
		}

		_catchUpBanner?.Close();

		// Трей — следующим: иконка исчезает раньше всего живого.
		_tray?.Dispose();

		// Стек — следующим (карточки и вотчер стола), затем монитор и отписка:
		// не перезагрузиться в момент разборки; опаздывающий BeginInvoke на
		// погашенном Dispatcher абортится молча.
		_stack?.Dispose();
		_rulesMonitor?.Dispose();
		if (_rules is not null)
		{
			_rules.Reloaded -= OnRulesReloaded;
		}

		_feed?.Dispose();

		// Э1 — последней: нативные баннеры возвращаются, когда фид уже молчит.
		// Dispose после Restore — страховочная сетка (no-op при успехе).
		if (_suppressor is not null)
		{
			_suppressor.Trace -= OnSuppressionTrace;
			try
			{
				_suppressor.Restore();
			}
			catch (Exception ex)
			{
				AddRow("Э1", $"возврат не удался: {ex.Message} — маркер оставлен, следующий старт починит");
			}

			_suppressor.Dispose();
		}

		base.OnClosed(e);

		// OnExplicitShutdown (S6.1): окно закрыто — жизнь процесса в наших руках.
		Application.Current.Shutdown();
	}

	// События фида приходят из пула потоков — маршалит на Dispatcher
	// (неблокирующе; discard — прецедент спайка S5.2).
	private void OnNotificationAdded(object? sender, NotificationAddedEventArgs e) => ProcessNotification(e.Record);

	// Общий путь живого события и «догоняющих» (S6.4): вердикт → карточка →
	// сносы → floor. Живое зовёт из пула, догонялка — с Dispatcher.
	private void ProcessNotification(NotificationRecord record, bool forceSticky = false, bool isCatchUp = false)
	{
		// Вердикт — один раз, здесь: движок immutable, потокобезопасен.
		RuleVerdict verdict = _rules?.Current.Decide(record) ?? RuleVerdict.CatchAll;
		if (forceSticky)
		{
			verdict = verdict with { Ttl = null }; // догоняющая ждёт читателя (S6.4)
		}
		// Имя/иконка отправителя — тоже здесь (реестр/PackageManager);
		// ImageSource заморожен резолвером — на Dispatcher только присваивание.
		SenderResolver.SenderInfo senderInfo = SenderResolver.Resolve(record.Aumid);
		uint id = record.Id;
		string text = $"{Describe(record)} ▸ {FormatVerdict(verdict)}";

		// BeginInvoke ДО Remove: очередь Dispatcher FIFO, «+» всегда выше «−»;
		// карточка show-вердикта открывается той же посылкой (стек — контракт
		// Dispatcher-only, record/verdict замкнуты, движок immutable).
		_ = Dispatcher.BeginInvoke(() =>
		{
			AddRow($"+ id {id}", text);
			if (verdict.Action == RuleAction.Show)
			{
				_stack?.Show(record, verdict, senderInfo, isCatchUp);
			}
		});
		// Новый отправитель — blanket ShowBanner=0 (Э1): супрессор под локом;
		// не блокирует показ карточки (трейс придёт своей строкой).
		_suppressor?.BlanketSender(record.Aumid);
		ApplyNativeRemoval(verdict, id);
		_floor?.MarkSeen(record);
		_history?.Add(new HistoryEntry(
			record.RaisedAt.ToUnixTimeSeconds(), record.Aumid, senderInfo.Name, record.Title, record.Body,
			verdict.Action.ToString().ToLowerInvariant()));
	}

	// S6.4 «догоняющие» (правило владельца: показать то, что не увидели).
	// Не увидел = нет во floor И формула висела погашенной мёртвым интервалом
	// (крах — RepairedFromCrash); чистый выход возвращал баннеры → юзер видел
	// нативно → молча в floor. Поток — пачками: очередь + pacing-таймер (карточка
	// раз в 1.5 с, стек-3 сам справляется), лимит settings.json catchUpLimit
	// (0 — не догонять), «Пропустить догоняющие» в меню трея сливает очередь.
	private void CatchUpMissed(IReadOnlyList<NotificationRecord> backlog)
	{
		if (_floor is null || _settings.CatchUpLimit <= 0)
		{
			foreach (NotificationRecord record in backlog)
			{
				_floor?.MarkSeen(record);
			}

			return;
		}

		bool missedWhileDead = _suppressor?.RepairedFromCrash == true;
		List<NotificationRecord> missed = [];
		foreach (NotificationRecord record in backlog)
		{
			if (missedWhileDead && !_floor.WasSeen(record))
			{
				missed.Add(record);
			}
			else
			{
				_floor.MarkSeen(record); // видел (floor) или нативно (чистый выход)
			}
		}

		if (missed.Count == 0)
		{
			return;
		}

		// Свежайшие вперёд (самое релевантное), хвост за лимитом — молча.
		missed.Sort((a, b) => b.RaisedAt.CompareTo(a.RaisedAt));
		int skipped = Math.Max(0, missed.Count - _settings.CatchUpLimit);
		foreach (NotificationRecord record in missed.Take(_settings.CatchUpLimit))
		{
			_catchUpQueue.Enqueue(record);
		}

		AddRow("Догон", $"очередь {_catchUpQueue.Count} пропущенных (лимит {_settings.CatchUpLimit}), "
			+ (_settings.CatchUpSticky ? "липкие — закрываешь ты, подача по слотам" : "поток пачками по 1.5с")
			+ "; скип — плашка над стеком или меню трея"
			+ (skipped > 0 ? $", за лимитом молча: {skipped}" : string.Empty));
		_catchUpBanner = new CatchUpBanner(_catchUpQueue.Count);
		_catchUpBanner.SkipRequested += OnTraySkipCatchUp; // тот же обработчик: слить в floor
		_catchUpBanner.Show();
		if (_stack is not null)
		{
			_stack.SlotFreed += OnCatchUpSlotFreed;
		}

		PumpCatchUp();
	}

	// Слот освободился (юзер закрыл карточку) — докладываем следующую догоняющую;
	// финал = очередь пуста И экранная догоняющая закрыта последней.
	private void OnCatchUpSlotFreed(object? sender, EventArgs e)
	{
		if (_catchUpQueue.Count > 0)
		{
			PumpCatchUp();
			return;
		}

		if (_stack is not null && _catchUpBanner is not null)
		{
			_catchUpBanner.MoveAbove(_stack.VisibleCount);
			if (!_stack.HasCatchUp)
			{
				FinishCatchUp();
			}
		}
	}

	// Конец потока: отписки и плашка вниз (с ANY пути — подача, таймер, слоты).
	private void FinishCatchUp()
	{
		if (_stack is not null)
		{
			_stack.SlotFreed -= OnCatchUpSlotFreed;
		}

		CloseCatchUpBanner();
	}

	// Подача по свободным слотам стека (липкий режим — читает юзер) или
	// по одной за тик 1.5с (не липкий: TTL вердикта сам двигает поток).
	private void PumpCatchUp()
	{
		if (_catchUpQueue.Count == 0)
		{
			FinishCatchUp();
			return;
		}

		// Липкий режим: заполняем стек сразу (показал 3 — закрываешь ты),
		// дальше подача по SlotFreed. ГОНКА: карточка ставится через
		// BeginInvoke — VisibleCount ещё старый, потому считаем показанное
		// локально; финиш здесь НЕ решаем (его закрытия/скип решат).
		if (_settings.CatchUpSticky)
		{
			int shown = 0;
			while (_catchUpQueue.Count > 0 && _stack is not null && _stack.VisibleCount + shown < CardStack.MaxCards)
			{
				NotificationRecord record = _catchUpQueue.Dequeue();
				AddRow("Догон", $"{Describe(record)} ▸ пропущено при мёртвой формуле");
				_catchUpBanner?.Update(_catchUpQueue.Count);
				ProcessNotification(record, forceSticky: true, isCatchUp: true);
				shown++;
			}

			_catchUpBanner?.MoveAbove(Math.Min(CardStack.MaxCards, (_stack?.VisibleCount ?? 0) + shown));
			return;
		}

		NotificationRecord paced = _catchUpQueue.Dequeue();
		AddRow("Догон", $"{Describe(paced)} ▸ пропущено при мёртвой формуле");
		_catchUpBanner?.Update(_catchUpQueue.Count);
		ProcessNotification(paced);
		if (_catchUpQueue.Count == 0)
		{
			_catchUpPacer?.Stop();
			FinishCatchUp(); // очередь иссякла; TTL-карточки сами уйдут, скипать нечего
		}

		if (!_settings.CatchUpSticky)
		{
			_catchUpPacer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
			if (_catchUpQueue.Count > 0 && !_catchUpPacer.IsEnabled)
			{
				_catchUpPacer.Tick -= PumpTimerTick; // страховка от двойной подписки
				_catchUpPacer.Tick += PumpTimerTick;
				_catchUpPacer.Start();
			}
		}
	}

	// Таймерный путь (не липкий): одна за тик.
	private void PumpTimerTick(object? sender, EventArgs e)
	{
		if (_catchUpQueue.Count == 0)
		{
			_catchUpPacer?.Stop();
			FinishCatchUp();
			return;
		}

		NotificationRecord record = _catchUpQueue.Dequeue();
		AddRow("Догон", $"{Describe(record)} ▸ пропущено при мёртвой формуле");
		_catchUpBanner?.Update(_catchUpQueue.Count);
		ProcessNotification(record);
		if (_catchUpQueue.Count == 0)
		{
			_catchUpPacer?.Stop();
			FinishCatchUp();
		}
	}

	private void CloseCatchUpBanner()
	{
		if (_catchUpBanner is null)
		{
			return;
		}

		_catchUpBanner.SkipRequested -= OnTraySkipCatchUp;
		_catchUpBanner.Close();
		_catchUpBanner = null;
	}

	// Меню трея «Пропустить догоняющие»: остаток очереди — молча в floor.
	private void OnTraySkipCatchUp(object? sender, EventArgs e)
	{
		// Скип meaningful и при пустой очереди: липкие догоняющие ещё на экране.
		if (_floor is null || _stack is null || (_catchUpQueue.Count == 0 && !_stack.HasCatchUp))
		{
			return;
		}

		int dropped = _catchUpQueue.Count;
		while (_catchUpQueue.Count > 0)
		{
			_floor.MarkSeen(_catchUpQueue.Dequeue());
		}

		_catchUpPacer?.Stop();
		_stack?.CloseCatchUp();
		FinishCatchUp();
		AddRow("Догон", $"скипнуто {dropped} + экран — в Центре (Win+N)");
	}

	// Трейс подавления: BlanketSender стреляет из пула (событие фида) — маршалит.
	private void OnSuppressionTrace(object? sender, SuppressionTraceEventArgs e)
	{
		_ = Dispatcher.BeginInvoke(() => AddRow("Э1", e.Message));
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
		AddRow(Strings.RowStack, e.Message);
	}

	private void OnSnapshotFailed(object? sender, NotificationFailedEventArgs e)
	{
		_ = Dispatcher.BeginInvoke(() => AddRow(Strings.RowSnapshot, $"ошибка: {e.Error.Message}"));
	}

	// Хот-релоад: движок уже подменён релоадером, окну остаётся сказать вслух.
	private void OnRulesReloaded(object? sender, RulesReloadedEventArgs e)
	{
		_ = Dispatcher.BeginInvoke(() => AddRow(
			Strings.RowRules,
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
			AddRow(Strings.RowMode, "packaged (MSIX)");
			AddRow("Name", id.Name);
			AddRow("Version", $"{id.Version.Major}.{id.Version.Minor}.{id.Version.Build}.{id.Version.Revision}");
			AddRow("Publisher", id.Publisher);
			AddRow("FamilyName", id.FamilyName);
			AddRow(
				Strings.RowIdentityMatch,
				string.Equals(id.Name, ProductIdentity.Name, StringComparison.Ordinal)
				&& string.Equals(id.Publisher, ProductIdentity.Publisher, StringComparison.Ordinal)
					? "да"
					: "НЕТ — манифест и код разошлись");
		}
		catch (InvalidOperationException)
		{
			// Package.Current вне пакета (unpackage F5-запуск) кидает — это норма.
			AddRow(Strings.RowMode, "не в пакете (unpackaged, F5)");
		}
	}

	// Копирует все строки панели в клипборд одной пачкой (владелец вставляет их
	// в сессию Claude). Буфер может быть занят чужим процессом — ExternalException.
	private void OnCopyClick(object sender, RoutedEventArgs e)
	{
		CopyButton.Content = Strings.CopyAll;
		try
		{
			Clipboard.SetText(string.Join(Environment.NewLine, _rows.Select(r => $"{r.Key} = {r.Value}")));
			CopyButton.Content = string.Format(CultureInfo.InvariantCulture, _copiedFormat, _rows.Count);
		}
		catch (System.Runtime.InteropServices.ExternalException ex)
		{
			CopyButton.Content = Strings.CopyFailedPrefix + ex.Message;
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
