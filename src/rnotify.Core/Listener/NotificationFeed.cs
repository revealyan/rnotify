using System.Diagnostics.CodeAnalysis;

namespace rnotify.Core.Listener;

/// <summary>
/// Прод-фид уведомлений: consent с таймаутом → включение сигнала изменений →
/// первый снапшот как базлайн (backlog молча пропускается — решение владельца
/// S2.1; дисковый floor отложен до появления рендера) → диффы снапшотов по Id,
/// сериализованные семафором (события хранилища летят пачками). Механика
/// перенесена из спайка S5.2 (Spike52Engine, факты Г2/Г3).
/// События поднимаются из пула потоков: WPF-потребитель маршалит на Dispatcher сам.
/// </summary>
[SuppressMessage("Design", "CA1031:Do not catch general exception types",
	Justification = "Фид переживает падение снапшота (HRESULT, отозванный доступ): ошибка уходит событием SnapshotFailed, фид продолжает жить")]
public sealed class NotificationFeed : IDisposable
{
	// Паритет со спайком S5.2: consent-диалогу даём минуту; по факту §10c ответ
	// приходит молча за ~80 мс, запас — на случай появления диалога.
	private static readonly TimeSpan _defaultConsentTimeout = TimeSpan.FromSeconds(60);

	private readonly INotificationSource _source;
	private readonly TimeSpan _consentTimeout;
	private readonly SemaphoreSlim _diffGate = new(initialCount: 1, maxCount: 1);
	private HashSet<uint> _seenIds = [];
	private bool _started;
	private volatile bool _disposed;

	/// <summary>Появилось уведомление, которого не было в прошлом снапшоте.</summary>
	public event EventHandler<NotificationAddedEventArgs>? Added;

	/// <summary>Уведомление исчезло из хранилища; ключ — только Id.</summary>
	public event EventHandler<NotificationRemovedEventArgs>? Removed;

	/// <summary>Снапшот упал: фид жив, ошибка — наружу для показа потребителем.</summary>
	public event EventHandler<NotificationFailedEventArgs>? SnapshotFailed;

	/// <summary>Создаёт фид поверх источника уведомлений.</summary>
	/// <param name="source">Шов над WinRT (фейк в тестах).</param>
	/// <param name="consentTimeout">Таймаут consent-диалога; тестам — миллисекунды.</param>
	public NotificationFeed(INotificationSource source, TimeSpan? consentTimeout = null)
	{
		_source = source;
		_consentTimeout = consentTimeout ?? _defaultConsentTimeout;
	}

	/// <summary>
	/// Единственный старт: WhenAny(RequestAccess, Delay) — при Allowed включается
	/// сигнал изменений и берётся базлайн (без событий, backlog пропускается);
	/// иначе статус наружу без подписки. Повторный вызов — InvalidOperationException.
	/// Базлайн берётся ПОД сигналом: диффы по событиям встают в очередь за семафором,
	/// изменения между consent и базлайном не теряются.
	/// </summary>
	public async Task<ListenerStartResult> StartAsync()
	{
		if (_started)
		{
			throw new InvalidOperationException("Фид уже запущен: старт однократный (создайте новый экземпляр).");
		}

		_started = true;
		Task<NotificationAccessStatus> request = _source.RequestAccessAsync();
		Task finished = await Task.WhenAny(request, Task.Delay(_consentTimeout)).ConfigureAwait(false);
		if (finished != request)
		{
			// Диалог не отвечен: наблюдение позднего ответа из спайка выкинуто
			// сознательно (диагностика, не продукт) — перезапуск приложения решает.
			return new ListenerStartResult(NotificationAccessStatus.TimedOut, BaselineCount: 0);
		}

		NotificationAccessStatus status = await request.ConfigureAwait(false);
		if (status != NotificationAccessStatus.Allowed)
		{
			return new ListenerStartResult(status, BaselineCount: 0);
		}

		_source.StartListening(OnSourceChanged);
		DiffResult baseline = await DiffAsync(raiseEvents: false).ConfigureAwait(false);
		return new ListenerStartResult(status, baseline.Count);
	}

	/// <summary>Отписка от источника и освобождение семафора; события больше не поднимаются.</summary>
	public void Dispose()
	{
		_disposed = true;
		_source.StopListening();
		_diffGate.Dispose();
	}

	// Сигналы летят пачками — дифф встаёт в очередь за семафором (discard:
	// исключения по периметру DiffAsync, прецедент спайка S5.2).
	private void OnSourceChanged()
	{
		if (_disposed)
		{
			return;
		}

		_ = DiffAsync(raiseEvents: true);
	}

	// Мутация _seenIds — только под семафором, отдельный lock не нужен.
	// Базлайн (raiseEvents=false) — первый снапшот: состояния до него нет,
	// события не поднимаются (backlog молча пропускается).
	private async Task<DiffResult> DiffAsync(bool raiseEvents)
	{
		await _diffGate.WaitAsync().ConfigureAwait(false);
		DiffResult result;
		try
		{
			IReadOnlyList<NotificationRecord> fresh = await _source.GetSnapshotAsync().ConfigureAwait(false);
			HashSet<uint> freshIds = fresh.Select(r => r.Id).ToHashSet();
			result = new DiffResult(
				fresh.Count,
				Added: fresh.Where(r => !_seenIds.Contains(r.Id)).ToArray(),
				Removed: _seenIds.Where(id => !freshIds.Contains(id)).ToArray());
			_seenIds = freshIds;
		}
		catch (Exception ex)
		{
			// Состояние не тронуто: следующий дифф сравнит с прежним базисом.
			SnapshotFailed?.Invoke(this, new NotificationFailedEventArgs(ex));
			return new DiffResult(0, [], []);
		}
		finally
		{
			_diffGate.Release();
		}

		// События — ПОСЛЕ Release: хендлеры не тормозят следующий дифф и не рвут его
		// своим исключением; сначала Added, потом Removed (стабильный порядок для UI).
		if (!raiseEvents)
		{
			return result;
		}

		foreach (NotificationRecord record in result.Added)
		{
			Added?.Invoke(this, new NotificationAddedEventArgs(record));
		}

		foreach (uint id in result.Removed)
		{
			Removed?.Invoke(this, new NotificationRemovedEventArgs(id));
		}

		return result;
	}

	private sealed record DiffResult(int Count, IReadOnlyList<NotificationRecord> Added, IReadOnlyList<uint> Removed);
}
