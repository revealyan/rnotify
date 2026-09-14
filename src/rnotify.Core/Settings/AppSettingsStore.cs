using System.Text.Encodings.Web;
using System.Text.Json;
using IOPath = System.IO.Path;

namespace rnotify.Core.Settings;

/// <summary>
/// Хранение settings.json. Дефолт — %USERPROFILE%\.rnotify\settings.json: из
/// пакета файл ложится в реальный профиль (unvirtualizedResources, канон §10c),
/// его удобно править руками. Файл читается один раз на старте (поле влияет
/// только на решение о применении формулы Э1) — хот-релоада нет, смена
/// настройки требует перезапуска. Битый json — дефолт НА ПАМЯТЬ (в отличие от
/// rules.json потери пользовательских данных нет: булево поле) и файл на диске
/// не трогаем; ошибка — наружу для панели.
/// </summary>
public sealed class AppSettingsStore
{
	// Файл правится руками: camelCase, прощаются комментарии, трейлинг-запятые
	// и регистр ключей; UnsafeRelaxedJsonEscaping — только запись дефолта.
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	/// <summary>Путь по умолчанию: %USERPROFILE%\.rnotify\settings.json.</summary>
	public static string DefaultPath { get; } = IOPath.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rnotify", "settings.json");

	/// <summary>Создаёт хранилище поверх конкретного файла.</summary>
	/// <param name="path">Путь к settings.json; null — <see cref="DefaultPath"/> (тесты подставляют временный).</param>
	public AppSettingsStore(string? path = null) => FilePath = path ?? DefaultPath;

	/// <summary>Путь к файлу настроек.</summary>
	public string FilePath { get; }

	/// <summary>Сохраняет настройки атомарно (tmp + Move) — чекбокс трея «Автозапуск».</summary>
	public void Save(AppSettings settings)
	{
		Directory.CreateDirectory(IOPath.GetDirectoryName(FilePath) ?? ".");
		string tmp = FilePath + ".tmp";
		using (FileStream stream = File.Create(tmp))
		{
			JsonSerializer.Serialize(stream, settings, _jsonOptions);
		}

		File.Move(tmp, FilePath, overwrite: true);
	}

	/// <summary>Читает настройки; файла нет — атомарно создаёт дефолтный и отдаёт его.</summary>
	public AppSettingsLoadResult LoadOrDefault()
	{
		if (!File.Exists(FilePath))
		{
			AppSettings created = new();
			try
			{
				Directory.CreateDirectory(IOPath.GetDirectoryName(FilePath) ?? ".");
				WriteAtomic(created);
				return new AppSettingsLoadResult(created, CreatedDefault: true, Error: null);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
			{
				// Записать дефолт не вышло — работаем на нём в памяти, ошибку скажем.
				return new AppSettingsLoadResult(created, CreatedDefault: false, Error: ex);
			}
		}

		try
		{
			using FileStream stream = File.OpenRead(FilePath);
			AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(stream, _jsonOptions);
			return new AppSettingsLoadResult(settings ?? new(), CreatedDefault: false, Error: null);
		}
		catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException
			or UnauthorizedAccessException or ArgumentException)
		{
			// Битый файл: файл не трогаем, работаем на дефолте, ошибку скажем.
			return new AppSettingsLoadResult(new(), CreatedDefault: false, Error: ex);
		}
	}

	private void WriteAtomic(AppSettings settings)
	{
		string tmp = FilePath + ".tmp";
		using (FileStream stream = File.Create(tmp))
		{
			JsonSerializer.Serialize(stream, settings, _jsonOptions);
		}

		File.Move(tmp, FilePath, overwrite: true);
	}
}
