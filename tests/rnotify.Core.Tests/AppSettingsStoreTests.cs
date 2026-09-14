using rnotify.Core.Settings;
using Xunit;

namespace rnotify.Core.Tests;

/// <summary>Хранилище настроек: создание дефолта, чтение, битый json — дефолт на память.</summary>
public sealed class AppSettingsStoreTests
{
	[Fact]
	public void Отсутствие_файла_создаёт_дефолт()
	{
		using TempDir dir = new();
		string path = dir.PathFor("settings.json");
		AppSettingsStore store = new(path);

		AppSettingsLoadResult result = store.LoadOrDefault();

		Assert.Null(result.Error);
		Assert.True(result.CreatedDefault);
		Assert.False(result.Settings.SuppressWithoutListener); // дефолт — безопасная ветка consent
		Assert.True(File.Exists(path));

		// Дефолт реально на диске: второе чтение парсит файл, не память.
		AppSettingsLoadResult second = store.LoadOrDefault();
		Assert.False(second.CreatedDefault);
		Assert.False(second.Settings.SuppressWithoutListener);
	}

	[Fact]
	public void Читает_поле_из_рукописного_json()
	{
		using TempDir dir = new();
		string path = dir.PathFor("settings.json");
		File.WriteAllText(path, """
			{
			  // гасить баннеры даже без согласия листенера
			  "suppressWithoutListener": true
			}
			""");
		AppSettingsStore store = new(path);

		AppSettingsLoadResult result = store.LoadOrDefault();

		Assert.Null(result.Error);
		Assert.False(result.CreatedDefault);
		Assert.True(result.Settings.SuppressWithoutListener);
	}

	[Fact]
	public void Битый_json_дефолт_на_память_файл_не_тронут()
	{
		using TempDir dir = new();
		string path = dir.PathFor("settings.json");
		File.WriteAllText(path, "{ кривой json");
		AppSettingsStore store = new(path);

		AppSettingsLoadResult result = store.LoadOrDefault();

		Assert.NotNull(result.Error);
		Assert.False(result.CreatedDefault);
		Assert.False(result.Settings.SuppressWithoutListener);
		Assert.Equal("{ кривой json", File.ReadAllText(path));
	}

	[Fact]
	public void Старый_файл_без_autostart_читается_дефолтом_поля()
	{
		using TempDir dir = new();
		string path = dir.PathFor("settings.json");
		File.WriteAllText(path, """{ "suppressWithoutListener": true }""");
		AppSettingsStore store = new(path);

		AppSettingsLoadResult result = store.LoadOrDefault();

		Assert.Null(result.Error);
		Assert.True(result.Settings.SuppressWithoutListener);
		Assert.False(result.Settings.Autostart); // поле пришло в S6.1 — старые файлы совместимы
		Assert.Null(result.Settings.Language);   // S6.2: без поля — язык ОС
	}

	[Fact]
	public void Save_пишет_autostart_и_перечитывается_без_мусора()
	{
		using TempDir dir = new();
		string path = dir.PathFor("settings.json");
		AppSettingsStore store = new(path);
		_ = store.LoadOrDefault(); // файл создан дефолтным

		store.Save(new AppSettings(SuppressWithoutListener: true, Autostart: true, Language: "ru"));

		AppSettingsLoadResult reread = store.LoadOrDefault();
		Assert.Null(reread.Error);
		Assert.True(reread.Settings.SuppressWithoutListener);
		Assert.True(reread.Settings.Autostart);
		Assert.Equal("ru", reread.Settings.Language); // S6.2: язык UI round-trip
		Assert.False(File.Exists(path + ".tmp")); // атомарно: tmp-мусора не остаётся
	}
}
