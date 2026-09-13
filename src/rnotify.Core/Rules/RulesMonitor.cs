using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace rnotify.Core.Rules;

/// <summary>
/// Хот-релоад: FileSystemWatcher на rules.json + дебаунс — редакторы пишут файл
/// долями (блокнот — в два захода), сигналы в окне дебаунса схлопываются в одну
/// перезагрузку (паттерн старого rnotif). Deleted игнорируется: атомарные
/// сейверы делают delete-then-create, Created прилетит следом. Переполнение
/// буфера watcher'а (Error) лечим немедленной перезагрузкой.
/// </summary>
public sealed class RulesMonitor : IDisposable
{
	/// <summary>Дефолтное окно дебаунса.</summary>
	public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

	private readonly RulesReloader _reloader;
	private readonly TimeSpan _debounce;
	private readonly Func<TimeSpan, Task> _delay;
	private readonly FileSystemWatcher _watcher;
	private int _pending; // 1 = перезагрузка уже запланирована (Interlocked)
	private volatile bool _disposed;

	/// <summary>Создаёт монитор и включает watcher.</summary>
	/// <param name="store">Хранилище: каталог и имя файла для watcher'а.</param>
	/// <param name="reloader">Кто перезагружает.</param>
	/// <param name="debounce">Окно схлопывания сигналов; тестам — произвольное.</param>
	/// <param name="delay">Ожидание; тесты подставляют управляемый Task (детерминизм без слипов).</param>
	public RulesMonitor(RulesStore store, RulesReloader reloader, TimeSpan? debounce = null, Func<TimeSpan, Task>? delay = null)
	{
		_reloader = reloader;
		_debounce = debounce ?? DefaultDebounce;
		_delay = delay ?? (static t => Task.Delay(t));

		_watcher = new FileSystemWatcher(
			Path.GetDirectoryName(store.FilePath) ?? ".",
			Path.GetFileName(store.FilePath))
		{
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
		};
		_watcher.Changed += OnFileEvent;
		_watcher.Created += OnFileEvent;
		_watcher.Renamed += OnFileEvent;
		_watcher.Error += OnWatcherError;
		_watcher.EnableRaisingEvents = true;
	}

	/// <summary>
	/// Сигнал «конфиг мог измениться» (watcher и тесты): первый сигнал в окне
	/// дебаунса планирует перезагрузку, остальные гаснут.
	/// </summary>
	public void SignalChange()
	{
		if (_disposed || Interlocked.Exchange(ref _pending, 1) == 1)
		{
			return;
		}

		_ = DebouncedReloadAsync();
	}

	/// <summary>Гасит watcher; отложенная перезагрузка не выполнится (проверка _disposed).</summary>
	public void Dispose()
	{
		_disposed = true;
		_watcher.EnableRaisingEvents = false;
		_watcher.Changed -= OnFileEvent;
		_watcher.Created -= OnFileEvent;
		_watcher.Renamed -= OnFileEvent;
		_watcher.Error -= OnWatcherError;
		_watcher.Dispose();
	}

	private void OnFileEvent(object sender, FileSystemEventArgs e) => SignalChange();

	// Переполнение буфера = потерянные события; надёжный рецепт — перезагрузиться.
	private void OnWatcherError(object sender, ErrorEventArgs e) => _reloader.ReloadNow();

	[SuppressMessage("Design", "CA1031:Do not catch general exception types",
		Justification = "Сломанный шов ожидания не должен отменять перезагрузку — окно считается истёкшим (ReloadNow сам не бросает)")]
	private async Task DebouncedReloadAsync()
	{
		try
		{
			await _delay(_debounce).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			// Управляемый тестовый delay сломан — считаем окно истёкшим; след
			// только в debug-сборке (Conditional), релиз не платит.
			Debug.WriteLine($"rules: шов ожидания перезагрузки сломан: {ex.Message}");
		}

		// Флаг сбрасываем ДО перезагрузки: сигналы, пришедшие во время неё,
		// планируют следующую.
		_pending = 0;
		if (!_disposed)
		{
			_reloader.ReloadNow();
		}
	}
}
