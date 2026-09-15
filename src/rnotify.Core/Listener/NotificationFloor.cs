using System.Text.Json;
using IOPath = System.IO.Path;

namespace rnotify.Core.Listener;

/// <summary>
/// Дисковый floor «что уже обработано» — %USERPROFILE%\.rnotify\seen.json
/// (S6.4). Ключ пары (Id, CreationTime): Id хранилища переиспользуется после
/// сноса, время отличает рециркуляции. Значение — unix-время записи: чистка
/// старше 7 дней при каждой записи, потолок 1000 свежих. Запись атомарная
/// (tmp + Move). Смысл (правило владельца): «пользователь должен увидеть то,
/// что не увидел» — по floor догоняющие карточки отличают «видели карточкой»
/// от «не видели вообще» (последнее — только если формула висела погашенной,
/// маркер краха). Битый файл = пустой floor (лучше один дубль, чем потеря).
/// </summary>
public sealed class NotificationFloor
{
	private static readonly TimeSpan _keep = TimeSpan.FromDays(7);
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
	};

	private readonly Lock _gate = new();
	private readonly string _filePath;
	private Dictionary<string, long> _seen = [];

	/// <summary>Путь по умолчанию: %USERPROFILE%\.rnotify\seen.json.</summary>
	public static string DefaultPath { get; } = IOPath.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rnotify", "seen.json");

	/// <summary>Создаёт floor поверх файла; файла нет — пустой (создастся первой записью).</summary>
	/// <param name="path">Путь seen.json; null — <see cref="DefaultPath"/> (тесты подставляют временный).</param>
	public NotificationFloor(string? path = null)
	{
		_filePath = path ?? DefaultPath;
		Load();
	}

	/// <summary>Было ли уже обработано (Id+время совпали).</summary>
	public bool WasSeen(NotificationRecord record) => WasSeen(record.Id, record.RaisedAt);

	/// <summary>Была ли уже обработана пара Id+время (без записи).</summary>
	public bool WasSeen(uint id, DateTimeOffset raisedAt)
	{
		lock (_gate)
		{
			return _seen.ContainsKey(Key(id, raisedAt));
		}
	}

	/// <summary>Пометить обработанным и сохранить на диск (одна атомарная запись).</summary>
	public void MarkSeen(NotificationRecord record) => MarkSeen(record.Id, record.RaisedAt);

	/// <summary>Пометить пару обработанной и сохранить на диск.</summary>
	public void MarkSeen(uint id, DateTimeOffset raisedAt)
	{
		lock (_gate)
		{
			_seen[Key(id, raisedAt)] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			SaveLocked();
		}
	}

	// (Id, CreationTime) → «id|ticks»: одна строка-ключ, чтобы не плодить JSON-структуры.
	private static string Key(uint id, DateTimeOffset raisedAt) => $"{id}|{raisedAt.UtcTicks}";

	private void Load()
	{
		try
		{
			if (!File.Exists(_filePath))
			{
				return;
			}

			using FileStream stream = File.OpenRead(_filePath);
			Dictionary<string, long>? loaded = JsonSerializer.Deserialize<Dictionary<string, long>>(stream, _jsonOptions);
			if (loaded is not null)
			{
				_seen = Prune(loaded);
			}
		}
		catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
		{
			// Битый floor = пустой: честнее один дубль, чем потерянная догонялка.
			_seen = [];
		}
	}

	private void SaveLocked()
	{
		Dictionary<string, long> pruned = Prune(_seen);
		_seen = pruned;
		try
		{
			Directory.CreateDirectory(IOPath.GetDirectoryName(_filePath) ?? ".");
			string tmp = _filePath + ".tmp";
			using (FileStream stream = File.Create(tmp))
			{
				JsonSerializer.Serialize(stream, pruned, _jsonOptions);
			}

			File.Move(tmp, _filePath, overwrite: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
		{
			// Диск недоступен: floor жив в памяти — догонялка работает в сессии,
			// между запусками может продублировать (приемлемая деградация).
			_ = ex;
		}
	}

	// Чистка по возрасту значения (unix-секунды) и потолок 1000 свежих.
	private static Dictionary<string, long> Prune(Dictionary<string, long> seen)
	{
		long cutoff = DateTimeOffset.UtcNow.Subtract(_keep).ToUnixTimeSeconds();
		Dictionary<string, long> fresh = [];
		foreach (KeyValuePair<string, long> entry in seen)
		{
			if (entry.Value >= cutoff)
			{
				fresh[entry.Key] = entry.Value;
			}
		}

		return fresh.Count <= 1000
			? fresh
			: fresh.OrderByDescending(e => e.Value).Take(1000).ToDictionary();
	}
}
