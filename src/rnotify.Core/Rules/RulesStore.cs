using System.Text.Encodings.Web;
using System.Text.Json;
using IOPath = System.IO.Path;

namespace rnotify.Core.Rules;

/// <summary>
/// Хранение rules.json. Дефолт — %USERPROFILE%\.rnotify\rules.json: из пакета
/// файл ложится в реальный профиль (unvirtualizedResources, канон §10c), его
/// удобно править руками. Запись атомарная (tmp + Move), чтобы хот-релоад не
/// видел половину файла. Битый json — ошибкой наружу; молчаливая подмена
/// дефолтом запрещена (грабля старого rnotif: опечатка стирала правила
/// пользователя до починки файла).
/// </summary>
public sealed class RulesStore
{
	// Файл правится руками: camelCase-запись; при чтении прощаются кириллица,
	// комментарии, трейлинг-запятые и регистр ключей. UnsafeRelaxedJsonEscaping —
	// только ЗАПИСЬ дефолта: без него кириллица имён групп уезжает в \u-эскейпы
	// (дефолтный encoder гонит весь не-ASCII), а файл человеку править.
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	/// <summary>Путь к конфигу по умолчанию: %USERPROFILE%\.rnotify\rules.json.</summary>
	public static string DefaultPath { get; } = IOPath.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rnotify", "rules.json");

	/// <summary>Создаёт хранилище поверх конкретного файла.</summary>
	/// <param name="path">Путь к rules.json; null — <see cref="DefaultPath"/> (тесты подставляют временный).</param>
	public RulesStore(string? path = null) => FilePath = path ?? DefaultPath;

	/// <summary>Путь к файлу конфига.</summary>
	public string FilePath { get; }

	/// <summary>Читает конфиг; файла нет — атомарно создаёт дефолтный и отдаёт его.</summary>
	public RulesLoadResult LoadOrDefault()
	{
		if (!File.Exists(FilePath))
		{
			RulesConfig created = CreateDefault();
			try
			{
				Directory.CreateDirectory(IOPath.GetDirectoryName(FilePath) ?? ".");
				WriteAtomic(created);
				return new RulesLoadResult(created, CreatedDefault: true, Error: null);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
			{
				// Записать дефолт не вышло — работаем на нём в памяти, ошибку скажем.
				return new RulesLoadResult(created, CreatedDefault: false, Error: ex);
			}
		}

		try
		{
			using FileStream stream = File.OpenRead(FilePath);
			RulesConfig config = JsonSerializer.Deserialize<RulesConfig>(stream, _jsonOptions)
				?? throw new JsonException("Файл правил пуст (json null).");
			return new RulesLoadResult(config, CreatedDefault: false, Error: null);
		}
		catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException
			or UnauthorizedAccessException or ArgumentException)
		{
			// Файл на диске не трогаем: правки пользователя не теряются.
			return new RulesLoadResult(null, CreatedDefault: false, Error: ex);
		}
	}

	/// <summary>Сохраняет конфиг атомарно (tmp в том же каталоге + Move поверх).</summary>
	public void Save(RulesConfig config)
	{
		Directory.CreateDirectory(IOPath.GetDirectoryName(FilePath) ?? ".");
		WriteAtomic(config);
	}

	/// <summary>
	/// Дефолтный конфиг: живые примеры всех трёх действий — тест-тосты
	/// (send-test-toast.ps1) летят с AUMID powershell, титулы mute/delete/kill.
	/// </summary>
	public static RulesConfig CreateDefault() => new()
	{
		Groups =
		[
			new RuleGroupConfig
			{
				Name = "Тихие",
				Color = "#8A8A8A",
				Rules =
				[
					new RuleConfig { Name = "mute-пример", App = "powershell", Title = "mute", Match = "contains", Action = "mute" },
				],
			},
			new RuleGroupConfig
			{
				Name = "Мусор",
				Rules =
				[
					new RuleConfig { Name = "delete-пример", App = "powershell", Title = "delete", Match = "contains", Action = "delete" },
					new RuleConfig { Name = "kill-пример", App = "powershell", Title = "kill", Match = "contains", Action = "show", Ttl = "3m", KillNative = true },
				],
			},
		],
	};

	private void WriteAtomic(RulesConfig config)
	{
		string tmp = FilePath + ".tmp";
		using (FileStream stream = File.Create(tmp))
		{
			JsonSerializer.Serialize(stream, config, _jsonOptions);
		}

		File.Move(tmp, FilePath, overwrite: true);
	}
}
