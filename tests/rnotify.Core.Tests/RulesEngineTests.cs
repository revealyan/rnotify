using rnotify.Core.Listener;
using rnotify.Core.Rules;
using Xunit;

namespace rnotify.Core.Tests;

/// <summary>
/// Компиляция и матчинг движка правил: режимы и поля, AND/wildcard, порядок и
/// первый матч, выбраковка с отчётом, ttl-парсинг, catch-all, regex-таймаут.
/// </summary>
public sealed class RulesEngineTests
{
	private static NotificationRecord Rec(string? aumid = "app.test/x", string title = "титул", string body = "тело") =>
		new(7, aumid, title, body, DateTimeOffset.UnixEpoch);

	private static RulesConfig Cfg(params RuleConfig[] rules) => new()
	{
		Groups = [new RuleGroupConfig { Name = "Группа", Rules = rules.ToList() }],
	};

	[Fact]
	public void Матчеры_по_полям_и_режимам_aumid_title_body()
	{
		(string Match, string App, string Title, string Body)[] cases =
		[
			("regex", "^app\\.", "^тит", "ело$"),
			("exact", "APP.TEST/X", "Титул", "Тело"),
			("contains", "test", "иту", "ел"),
		];
		foreach ((string match, string app, string title, string body) in cases)
		{
			RulesEngine engine = RulesEngine.Compile(Cfg(new RuleConfig
			{
				Name = "r",
				App = app,
				Title = title,
				Body = body,
				Match = match,
			}));
			Assert.Equal("r", engine.Decide(Rec()).RuleName);
		}

		// app матчит ТОЛЬКО по AUMID: паттерн текста заголовка в app не совпадает.
		RulesEngine byTitleAsApp = RulesEngine.Compile(Cfg(new RuleConfig { App = "титул" }));
		Assert.Null(byTitleAsApp.Decide(Rec()).RuleName);
	}

	[Fact]
	public void AND_семантика_null_поле_wildcard()
	{
		// Только title задан: app/body — wildcard.
		RulesEngine onlyTitle = RulesEngine.Compile(Cfg(new RuleConfig { Name = "r", Title = "срочно" }));
		Assert.Equal("r", onlyTitle.Decide(Rec(title: "СРОЧНО")).RuleName); // regex IgnoreCase

		// Поля — AND: title подходит, body нет → нет матча.
		RulesEngine and = RulesEngine.Compile(Cfg(new RuleConfig { Title = "срочно", Body = "чужое" }));
		Assert.Same(RuleVerdict.CatchAll, and.Decide(Rec(title: "срочно", body: "своё")));

		// AUMID нет (null): непустой app-паттерн не матчится, отсутствующий — wildcard.
		RulesEngine strictApp = RulesEngine.Compile(Cfg(new RuleConfig { App = "app" }));
		Assert.Same(RuleVerdict.CatchAll, strictApp.Decide(Rec(aumid: null)));
		RulesEngine noApp = RulesEngine.Compile(Cfg(new RuleConfig { Name = "r", Title = "титул" }));
		Assert.Equal("r", noApp.Decide(Rec(aumid: null)).RuleName);
	}

	[Fact]
	public void Порядок_файла_побеждает_первый_матч()
	{
		RulesEngine engine = RulesEngine.Compile(new RulesConfig
		{
			Groups =
			[
				new() { Name = "Первая", Rules = [new RuleConfig { Name = "раньше", Action = "mute" }] },
				new() { Name = "Вторая", Rules = [new RuleConfig { Name = "позже", Action = "delete" }] },
			],
		});

		RuleVerdict verdict = engine.Decide(Rec());

		Assert.Equal(("Первая", "раньше", RuleAction.Mute), (verdict.GroupName, verdict.RuleName, verdict.Action));
	}

	[Fact]
	public void Выключенная_группа_пропускается()
	{
		RulesEngine engine = RulesEngine.Compile(new RulesConfig
		{
			Groups =
			[
				new() { Name = "Выкл", Enabled = false, Rules = [new RuleConfig { Name = "спит", Action = "delete" }] },
				new() { Name = "Вкл", Rules = [new RuleConfig { Name = "жив", Action = "mute" }] },
			],
		});

		Assert.Equal(1, engine.RuleCount);
		Assert.Equal(1, engine.DisabledRuleCount);
		Assert.Empty(engine.Discarded); // пропущено, не выбраковано
		RuleVerdict verdict = engine.Decide(Rec());
		Assert.Equal(("Вкл", "жив", RuleAction.Mute), (verdict.GroupName, verdict.RuleName, verdict.Action));
	}

	[Fact]
	public void Выбраковка_кривых_правил_с_отчётом()
	{
		RulesEngine engine = RulesEngine.Compile(new RulesConfig
		{
			Groups =
			[
				new()
				{
					Name = "G",
					Rules =
					[
						new RuleConfig { Name = "кривой regex", App = "(" },
						new RuleConfig { Name = "левый action", Action = "delete-all" },
						new RuleConfig { Name = "левый match", Match = "glob" },
						new RuleConfig { Name = "левый click", Click = "double" },
						new RuleConfig { Name = "живой" },
					],
				},
			],
		});

		Assert.Equal(1, engine.RuleCount);
		Assert.Equal(4, engine.Discarded.Count);
		Assert.All(engine.Discarded, d => Assert.Equal("G", d.GroupName));
		Assert.Contains(engine.Discarded, d => string.Equals(d.RuleName, "кривой regex", StringComparison.Ordinal) && d.Reason.Contains("regex", StringComparison.Ordinal));
		Assert.Contains(engine.Discarded, d => string.Equals(d.RuleName, "левый action", StringComparison.Ordinal));
		Assert.Equal("живой", engine.Decide(Rec()).RuleName); // сосед жив
	}

	[Fact]
	public void Ноль_правил_и_нет_матча_дают_catch_all()
	{
		RulesEngine empty = RulesEngine.Compile(new RulesConfig());
		RulesEngine noMatch = RulesEngine.Compile(Cfg(new RuleConfig { Name = "r", Title = "чужое" }));

		RuleVerdict fromEmpty = empty.Decide(Rec());
		RuleVerdict unmatched = noMatch.Decide(Rec());
		Assert.Same(RuleVerdict.CatchAll, fromEmpty);
		Assert.Same(RuleVerdict.CatchAll, unmatched);

		foreach (RuleVerdict verdict in new[] { fromEmpty, unmatched })
		{
			Assert.Equal(RuleAction.Show, verdict.Action);
			Assert.Equal(RulesEngine.DefaultShowTtl, verdict.Ttl);
			Assert.Null(verdict.GroupName);
			Assert.False(verdict.RequiresNativeRemoval);
		}
	}

	[Fact]
	public void Regex_таймаут_равен_не_совпадению_правило_живо()
	{
		RulesEngine engine = RulesEngine.Compile(
			Cfg(new RuleConfig { Name = "злой", Body = "(a+)+$" }),
			regexMatchTimeout: TimeSpan.FromMilliseconds(1));

		Assert.Empty(engine.Discarded); // паттерн скомпилировался, правило живо

		// Катастрофический бэктрекинг обрывается таймаутом ≈ «не совпало»: не виснет.
		RuleVerdict verdict = engine.Decide(Rec(body: new string('a', 40) + "!"));
		Assert.Same(RuleVerdict.CatchAll, verdict);
	}

	[Fact]
	public void Вердикт_несёт_поля_v2()
	{
		RulesEngine engine = RulesEngine.Compile(new RulesConfig
		{
			Groups =
			[
				new()
				{
					Name = "Группа",
					Color = "#7C3AED",
					Rules =
					[
						new RuleConfig { Name = "правило", Action = "show", Ttl = "3m", KillNative = true, OverFullscreen = true, Click = "focus" },
					],
				},
			],
		});

		RuleVerdict verdict = engine.Decide(Rec());
		Assert.Equal((RuleAction.Show, "Группа", "правило", "#7C3AED"), (verdict.Action, verdict.GroupName, verdict.RuleName, verdict.AccentHex));
		Assert.Equal(TimeSpan.FromMinutes(3), verdict.Ttl);
		Assert.Equal((true, true, ClickAction.Focus), (verdict.KillNative, verdict.OverFullscreen, verdict.Click));
		Assert.True(verdict.RequiresNativeRemoval); // killNative

		// delete требует сноса нативной копии, mute — нет.
		Assert.True(RulesEngine.Compile(Cfg(new RuleConfig { Action = "delete" })).Decide(Rec()).RequiresNativeRemoval);
		Assert.False(RulesEngine.Compile(Cfg(new RuleConfig { Action = "mute" })).Decide(Rec()).RequiresNativeRemoval);
	}

	[Fact]
	public void Ttl_парсинг_регистр_мусор_sticky_кламп()
	{
		TimeSpan? Ttl(string? raw) => RulesEngine.Compile(Cfg(new RuleConfig { Name = "r", Ttl = raw })).Decide(Rec()).Ttl;

		Assert.Equal(TimeSpan.FromSeconds(5), Ttl("5S")); // регистр не важен (грабля старого rnotif)
		Assert.Equal(TimeSpan.FromMinutes(3), Ttl("3m"));
		Assert.Equal(TimeSpan.FromSeconds(5), Ttl("мусор"));
		Assert.Equal(TimeSpan.FromSeconds(5), Ttl(null));
		Assert.Null(Ttl("STICKY"));
		Assert.Equal(TimeSpan.FromSeconds(1), Ttl("0s")); // clamp снизу
		Assert.Equal(TimeSpan.FromHours(1), Ttl("9999s")); // clamp сверху
	}
}
