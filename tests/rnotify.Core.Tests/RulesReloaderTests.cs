using rnotify.Core.Listener;
using rnotify.Core.Rules;
using Xunit;

namespace rnotify.Core.Tests;

/// <summary>Релоадер: подмена движка при успехе, удержание прежнего при ошибке, событие с отчётом.</summary>
public sealed class RulesReloaderTests
{
	private static RulesConfig Cfg(string group, string ruleName, string title) => new()
	{
		Groups = [new() { Name = group, Rules = [new RuleConfig { Name = ruleName, Title = title }] }],
	};

	private static RuleVerdict Decide(RulesEngine engine, string title) =>
		engine.Decide(new NotificationRecord(1, "aumid", title, string.Empty, DateTimeOffset.UnixEpoch));

	[Fact]
	public void Перезагрузка_подменяет_движок_отчёт_в_событии()
	{
		using TempRulesDir dir = new();
		RulesStore store = new(dir.RulesPath);
		store.Save(Cfg("A", "правило-A", "альфа"));
		RulesReloader reloader = new(store);
		Assert.Null(reloader.StartupError);

		List<RulesReloadedEventArgs> reloads = [];
		reloader.Reloaded += (_, e) => reloads.Add(e);

		store.Save(Cfg("B", "правило-B", "бета"));
		reloader.ReloadNow();

		Assert.Null(Decide(reloader.Current, "альфа").RuleName); // прежнее правило ушло
		Assert.Equal("правило-B", Decide(reloader.Current, "бета").RuleName);
		RulesReloadedEventArgs args = Assert.Single(reloads);
		Assert.True(args.Success);
		Assert.Same(reloader.Current, args.Engine);
	}

	[Fact]
	public void Битый_файл_держит_прежний_движок_ошибкой_наружу()
	{
		using TempRulesDir dir = new();
		RulesStore store = new(dir.RulesPath);
		store.Save(Cfg("A", "правило-A", "альфа"));
		RulesReloader reloader = new(store);
		RulesEngine before = reloader.Current;

		List<RulesReloadedEventArgs> reloads = [];
		reloader.Reloaded += (_, e) => reloads.Add(e);
		File.WriteAllText(dir.RulesPath, "{ кривой");
		reloader.ReloadNow();

		Assert.Same(before, reloader.Current); // прежние правила работают
		Assert.Equal("правило-A", Decide(reloader.Current, "альфа").RuleName);
		RulesReloadedEventArgs args = Assert.Single(reloads);
		Assert.False(args.Success);
		Assert.NotNull(args.Error);
		Assert.Same(before, args.Engine);
	}
}
