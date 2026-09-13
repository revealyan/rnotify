using System.Text.Json;
using rnotify.Core.Rules;
using Xunit;

namespace rnotify.Core.Tests;

/// <summary>Хранилище правил: создание дефолта, терпимость чтения, битый json, атомарная запись.</summary>
public sealed class RulesStoreTests
{
	[Fact]
	public void Отсутствие_файла_создаёт_дефолт()
	{
		using TempRulesDir dir = new();
		RulesStore store = new(dir.RulesPath);

		RulesLoadResult result = store.LoadOrDefault();

		Assert.True(result.Success);
		Assert.True(result.CreatedDefault);
		Assert.Null(result.Error);
		Assert.True(File.Exists(dir.RulesPath));

		// Дефолт реально на диске: второе чтение парсит файл, не память.
		RulesLoadResult second = store.LoadOrDefault();
		Assert.False(second.CreatedDefault);
		Assert.NotEmpty(second.Config!.Groups);
	}

	[Fact]
	public void Чтение_прощает_комментарии_запятые_и_регистр_ключей()
	{
		using TempRulesDir dir = new();
		RulesStore store = new(dir.RulesPath);
		File.WriteAllText(dir.RulesPath, """
			{
			  // рукописный конфиг
			  "groups": [
			    { "Name": "Руки", "rules": [ { "name": "правило", "app": "rhub", "action": "mute", } ] },
			  ]
			}
			""");

		RulesLoadResult result = store.LoadOrDefault();

		Assert.True(result.Success);
		RuleGroupConfig group = Assert.Single(result.Config!.Groups);
		Assert.Equal("Руки", group.Name);
		RuleConfig rule = Assert.Single(group.Rules);
		Assert.Equal(("rhub", "mute"), (rule.App, rule.Action));
	}

	[Fact]
	public void Битый_json_даст_ошибку_конфиг_пуст_файл_не_тронут()
	{
		using TempRulesDir dir = new();
		RulesStore store = new(dir.RulesPath);
		File.WriteAllText(dir.RulesPath, "{ кривой json");

		RulesLoadResult result = store.LoadOrDefault();

		Assert.False(result.Success);
		Assert.Null(result.Config);
		Assert.IsType<JsonException>(result.Error);
		Assert.Equal("{ кривой json", File.ReadAllText(dir.RulesPath)); // правки не перезаписаны
	}

	[Fact]
	public void Save_перезаписывает_атомарно_без_остатков()
	{
		using TempRulesDir dir = new();
		RulesStore store = new(dir.RulesPath);

		store.Save(new RulesConfig { Groups = [new() { Name = "Первая" }] });
		store.Save(new RulesConfig { Groups = [new() { Name = "Вторая" }] });

		// Кириллица — живыми буквами, не \u-эскейпами: файл правится руками.
		Assert.Contains("Вторая", File.ReadAllText(dir.RulesPath), StringComparison.Ordinal);
		Assert.Equal("rules.json", Path.GetFileName(Assert.Single(Directory.GetFiles(dir.DirPath)))); // tmp-остатков нет
		RulesLoadResult result = store.LoadOrDefault();
		Assert.Equal("Вторая", Assert.Single(result.Config!.Groups).Name);
	}
}
