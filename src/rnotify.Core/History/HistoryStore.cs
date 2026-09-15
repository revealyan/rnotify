using System.Text.Json;
using IOPath = System.IO.Path;

namespace rnotify.Core.History;

/// <summary>
/// Хранилище истории — %USERPROFILE%\.rnotify\history.json: список записей
/// старые→новые, добавление в конец, чистка до лимита (свежайшие остаются;
/// лимит — настройка historyLimit, дефолт 1000). Запись атомарная
/// (tmp + Move), на КАЖДОЕ уведомление (как floor — частота потока не
/// страшна). Потоки: Add зовётся из пула событий фида — всё под локом.
/// Битый файл = пустая история (история — сервисная штука, не терять из-за
/// неё поток).
/// </summary>
public sealed class HistoryStore
{
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
	};

	private readonly Lock _gate = new();
	private readonly string _filePath;
	private readonly int _limit;
	private List<HistoryEntry> _entries = [];

	/// <summary>Путь по умолчанию: %USERPROFILE%\.rnotify\history.json.</summary>
	public static string DefaultPath { get; } = IOPath.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rnotify", "history.json");

	/// <summary>Создаёт хранилище поверх файла (нет — пустое, создастся первой записью).</summary>
	/// <param name="limit">Потолок записей (настройка historyLimit; &lt;=0 — не храним вовсе).</param>
	/// <param name="path">Путь history.json; null — <see cref="DefaultPath"/> (тесты подставляют временный).</param>
	public HistoryStore(int limit, string? path = null)
	{
		_limit = limit;
		_filePath = path ?? DefaultPath;
		Load();
	}

	/// <summary>Записать уведомление в историю (чистка до лимита, атомарный сейв).</summary>
	public void Add(HistoryEntry entry)
	{
		if (_limit <= 0)
		{
			return; // история выключена настройкой
		}

		lock (_gate)
		{
			_entries.Add(entry);
			if (_entries.Count > _limit)
			{
				_entries.RemoveRange(0, _entries.Count - _limit);
			}

			SaveLocked();
		}
	}

	/// <summary>Снимок для панели: свежайшие вперёд.</summary>
	public IReadOnlyList<HistoryEntry> SnapshotNewestFirst()
	{
		lock (_gate)
		{
			return _entries.AsEnumerable().Reverse().ToArray();
		}
	}

	private void Load()
	{
		try
		{
			if (!File.Exists(_filePath))
			{
				return;
			}

			using FileStream stream = File.OpenRead(_filePath);
			List<HistoryEntry>? loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(stream, _jsonOptions);
			if (loaded is not null)
			{
				_entries = loaded;
			}
		}
		catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
		{
			_entries = []; // битая история — не повод ронять поток уведомлений
		}
	}

	private void SaveLocked()
	{
		try
		{
			Directory.CreateDirectory(IOPath.GetDirectoryName(_filePath) ?? ".");
			string tmp = _filePath + ".tmp";
			using (FileStream stream = File.Create(tmp))
			{
				JsonSerializer.Serialize(stream, _entries, _jsonOptions);
			}

			File.Move(tmp, _filePath, overwrite: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
		{
			// Диск недоступен: живём в памяти, между запусками история
			// обнулится — сервисная деградация, не фатальная.
			_ = ex;
		}
	}
}
