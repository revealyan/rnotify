using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using IOPath = System.IO.Path;

namespace rnotify.Core.Suppression;

/// <summary>
/// Формула Э1 в продукте (канон §10): глобально NOC_GLOBAL_SETTING_TOASTS_ENABLED=0
/// + blanket per-app ShowBanner=0 всем отправителям — доставка в хранилище жива,
/// корм листенера цел. Apply фотографирует прежнее состояние в маркер
/// suppression.json ДО записей: крах процесса маркер оставляет, следующий старт
/// чинит реестр и применяет заново. Возврат: глобальный prior был 0/1 → пишем
/// его, не было → явная единица (§10a: удаление значения не будит внутреннее
/// состояние шелла — баннеры остались бы мёртвыми); per-app prior был → пишем,
/// не было → DeleteValue (возврат баннера валидирован парой опыт/контроль S5.2).
/// Потоки: Apply/Restore — вызывающий (UI); BlanketSender — пул (событие фида),
/// поэтому всё состояние под локом; трейс может прилететь из пула — WPF-подписчик
/// маршалит сам.
/// </summary>
public sealed class NativeBannerSuppressor : IDisposable
{
	/// <summary>Глобальный тумблер баннеров в корне куста Settings.</summary>
	public const string GlobalToastsEnabledName = "NOC_GLOBAL_SETTING_TOASTS_ENABLED";

	/// <summary>Per-app тумблер баннеров в подключе отправителя.</summary>
	public const string ShowBannerName = "ShowBanner";

	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
	};

	private readonly INotificationSettingsRegistry _registry;
	private readonly string _markerPath;
	private readonly Lock _gate = new();
	private SuppressionSnapshot? _applied; // под _gate: активное подавление (null — не применяли/сняли)
	private HashSet<string> _blanketed = []; // под _gate: кому уже написан ShowBanner=0

	/// <summary>Строка трейса для панели диагностики.</summary>
	public event EventHandler<SuppressionTraceEventArgs>? Trace;

	/// <summary>Маркер по умолчанию: %USERPROFILE%\.rnotify\suppression.json.</summary>
	public static string DefaultMarkerPath { get; } = IOPath.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rnotify", "suppression.json");

	/// <summary>Создаёт супрессор поверх шва реестра.</summary>
	/// <param name="registry">Шов (прод — HKCU, тесты — фейк).</param>
	/// <param name="markerPath">Путь маркера-снапшота; null — <see cref="DefaultMarkerPath"/> (тесты подставляют временный).</param>
	public NativeBannerSuppressor(INotificationSettingsRegistry registry, string? markerPath = null)
	{
		_registry = registry;
		_markerPath = markerPath ?? DefaultMarkerPath;
	}

	/// <summary>
	/// Применить формулу: авторепейр по чужому маркеру (крах прошлой сессии) →
	/// снимок priors → маркер на диск → глобальный 0 → blanket всем существующим.
	/// Повторный вызов — тихо. Исключение на полпути — откат написанного и
	/// наружу: приложение продолжает работу без подавления (панель покажет).
	/// </summary>
	public void Apply()
	{
		lock (_gate)
		{
			if (_applied is not null)
			{
				return;
			}

			SuppressionSnapshot? stale = TryReadMarker();
			if (stale is not null)
			{
				// Неудача репейра хоронит единственный снапшот прошлой сессии —
				// наружу, свежий Apply поверх поломанного состояния не пишем.
				RestoreCore(stale);
				TryDeleteMarker();
				Trace?.Invoke(this, new SuppressionTraceEventArgs("Э1: маркер прошлой сессии — реестр отремонтирован перед применением"));
			}

			IReadOnlyList<string> senders = _registry.GetSenderKeys();
			Dictionary<string, int?> priors = [];
			foreach (string aumid in senders)
			{
				priors[aumid] = _registry.GetSenderDword(aumid, ShowBannerName);
			}
			SuppressionSnapshot snapshot = new(_registry.GetRootDword(GlobalToastsEnabledName), priors);

			// Маркер ДО записей: крах посреди blanket оставит репею прежние значения.
			WriteMarker(snapshot);
			try
			{
				_registry.SetRootDword(GlobalToastsEnabledName, 0);
				foreach (string aumid in senders)
				{
					_registry.SetSenderDword(aumid, ShowBannerName, 0);
				}
			}
			catch (Exception)
			{
				// Откат написанного; неудачный откат маркер не стирает — старт починит.
				if (TryRestoreCoreQuietly(snapshot))
				{
					TryDeleteMarker();
				}
				throw;
			}

			_applied = snapshot;
			_blanketed = [.. senders];
			Trace?.Invoke(this, new SuppressionTraceEventArgs($"Э1 применена: глобальный тумблер 0 + ShowBanner=0 ({senders.Count} отправителей)"));
		}
	}

	/// <summary>
	/// Новый отправитель с фида (поток — пул): ShowBanner=0 и включение в маркер,
	/// если ещё не покрыт blanket'ом. Null/пустой AUMID — тихо мимо (не с чем
	/// матчить). Не бросает: сбой — строка трейса, глобальный тумблер продолжает
	/// душить обычные тосты (не-reminder) и без per-app ключа.
	/// </summary>
	public void BlanketSender(string? aumid)
	{
		if (string.IsNullOrEmpty(aumid))
		{
			return;
		}

		lock (_gate)
		{
			if (_applied is null || _blanketed.Contains(aumid, StringComparer.Ordinal))
			{
				return;
			}

			try
			{
				int? prior = _registry.GetSenderDword(aumid, ShowBannerName);
				Dictionary<string, int?> extended = new(_applied.AppShowBanner, StringComparer.Ordinal)
				{
					[aumid] = prior,
				};
				SuppressionSnapshot updated = _applied with { AppShowBanner = extended };
				WriteMarker(updated);
				_registry.SetSenderDword(aumid, ShowBannerName, 0);
				_applied = updated;
				_blanketed.Add(aumid);
				Trace?.Invoke(this, new SuppressionTraceEventArgs($"Э1: новый отправитель {DescribeSender(aumid)} (ShowBanner=0)"));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
			{
				Trace?.Invoke(this, new SuppressionTraceEventArgs($"Э1: не удалось погасить нового отправителя {DescribeSender(aumid)}: {ex.Message}"));
			}
		}
	}

	/// <summary>Вернуть прежние значения (штатный выход). Тихо, если не применяли.</summary>
	public void Restore()
	{
		lock (_gate)
		{
			if (_applied is null)
			{
				return;
			}

			int count = _applied.AppShowBanner.Count;
			RestoreCore(_applied);
			TryDeleteMarker();
			_applied = null;
			_blanketed = [];
			Trace?.Invoke(this, new SuppressionTraceEventArgs($"Э1 снята: прежние значения возвращены ({count} отправителей)"));
		}
	}

	/// <inheritdoc/>
	[SuppressMessage("Design", "CA1031:Do not catch general exception types",
		Justification = "Страховочная сетка Dispose: неудача возврата не должна ронять выход приложения — маркер остаётся, следующий старт чинит")]
	public void Dispose()
	{
		lock (_gate)
		{
			if (_applied is null)
			{
				return;
			}

			try
			{
				RestoreCore(_applied);
				TryDeleteMarker();
			}
			catch (Exception ex)
			{
				Trace?.Invoke(this, new SuppressionTraceEventArgs($"Э1: возврат при Dispose не удался — маркер оставлен, старт починит: {ex.Message}"));
			}
			finally
			{
				_applied = null;
				_blanketed = [];
			}
		}
	}

	// Чистые записи возврата по снимку: глобальный — prior ?? 1 (§10a: удаление
	// не будит шелл, явная единица — семантический дефолт «баннеры включены»);
	// per-app — prior был значением → пишем его, не было → DeleteValue.
	private void RestoreCore(SuppressionSnapshot snapshot)
	{
		_registry.SetRootDword(GlobalToastsEnabledName, snapshot.GlobalToastsEnabled ?? 1);
		foreach (KeyValuePair<string, int?> prior in snapshot.AppShowBanner)
		{
			if (prior.Value is int value)
			{
				_registry.SetSenderDword(prior.Key, ShowBannerName, value);
			}
			else
			{
				_registry.DeleteSenderValue(prior.Key, ShowBannerName);
			}
		}
	}

	private bool TryRestoreCoreQuietly(SuppressionSnapshot snapshot)
	{
		try
		{
			RestoreCore(snapshot);
			return true;
		}
		catch (Exception ex)
		{
			Trace?.Invoke(this, new SuppressionTraceEventArgs($"Э1: откат после сбоя не удался — маркер оставлен, старт починит: {ex.Message}"));
			return false;
		}
	}

	private SuppressionSnapshot? TryReadMarker()
	{
		try
		{
			if (!File.Exists(_markerPath))
			{
				return null;
			}

			using FileStream stream = File.OpenRead(_markerPath);
			SuppressionSnapshot? snapshot = JsonSerializer.Deserialize<SuppressionSnapshot>(stream, _jsonOptions);
			if (snapshot is null)
			{
				// json «null» починить ничего не может — чтобы не молчал каждый старт.
				TryDeleteMarker();
			}
			return snapshot;
		}
		catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
		{
			// Битый маркер бесполезен для репейра — убираем, авторепейр пропускаем.
			Trace?.Invoke(this, new SuppressionTraceEventArgs($"Э1: маркер не читается, авторепейр пропущен ({ex.Message})"));
			TryDeleteMarker();
			return null;
		}
	}

	// Атомарно (tmp + Move), как RulesStore: крах посреди записи не оставляет половину.
	private void WriteMarker(SuppressionSnapshot snapshot)
	{
		Directory.CreateDirectory(IOPath.GetDirectoryName(_markerPath) ?? ".");
		string tmp = _markerPath + ".tmp";
		using (FileStream stream = File.Create(tmp))
		{
			JsonSerializer.Serialize(stream, snapshot, _jsonOptions);
		}

		File.Move(tmp, _markerPath, overwrite: true);
	}

	private void TryDeleteMarker()
	{
		try
		{
			File.Delete(_markerPath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
		{
			// Удалить не вышло: следующий старт увидит маркер и отрепейрит по нему
			// идемпотентно — не страшно.
			Trace?.Invoke(this, new SuppressionTraceEventArgs($"Э1: маркер не удался с диска ({ex.Message}) — следующий старт перепроверит"));
		}
	}

	// Хвост AUMID для читаемости трейс-строк (полные — с SID-путями — длинны).
	private static string DescribeSender(string aumid)
	{
		int tail = Math.Max(aumid.LastIndexOf('\\'), aumid.LastIndexOf('/'));
		return tail >= 0 && tail < aumid.Length - 1 ? aumid[(tail + 1)..] : aumid;
	}
}
