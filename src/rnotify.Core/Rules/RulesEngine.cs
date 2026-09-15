using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using rnotify.Core.Listener;

namespace rnotify.Core.Rules;

/// <summary>
/// Скомпилированный набор правил: группы разворачиваются в упорядоченный список,
/// побеждает первый матч; ноль правил или нет матча —
/// <see cref="RuleVerdict.CatchAll"/> (show 5 с: непокрытое правилами обязано
/// быть видимым, диспетчер не глушит молча). Неизменяем после создания — Decide
/// потокобезопасен по построению, хот-релоад подменяет весь движок. Кривые
/// правила выбраковываются с отчётом <see cref="Discarded"/>, не молча.
/// </summary>
public sealed class RulesEngine
{
	/// <summary>Дефолтный показ — 5 секунд, как системный баннер по умолчанию.</summary>
	public static readonly TimeSpan DefaultShowTtl = TimeSpan.FromSeconds(5);

	// Катастрофический бэктрекинг не должен вешать поток: таймаут на regex-матч;
	// превышение трактуется как «не совпало», правило остаётся живым (грабля
	// старого rnotif: Decide звался с UI-потока без таймаута).
	private static readonly TimeSpan _defaultRegexTimeout = TimeSpan.FromMilliseconds(100);

	private readonly IReadOnlyList<CompiledRule> _rules;

	private RulesEngine(CompiledRule[] rules, int disabledRuleCount, DiscardedRule[] discarded)
	{
		_rules = rules;
		RuleCount = rules.Length;
		DisabledRuleCount = disabledRuleCount;
		Discarded = discarded;
	}

	/// <summary>Число скомпилированных правил (правила выключенных групп не в счёт).</summary>
	public int RuleCount { get; }

	/// <summary>Сколько правил пропущено вместе с выключенными группами (пропущены, не выбракованы).</summary>
	public int DisabledRuleCount { get; }

	/// <summary>Отчёт о выбраковке: кривой regex, неизвестные match/action/click.</summary>
	public IReadOnlyList<DiscardedRule> Discarded { get; }

	/// <summary>Движок-дефолт: показывать всё 5 с (когда конфига нет вовсе).</summary>
	public static RulesEngine CreateDefault() => Compile(RulesStore.CreateDefault());

	/// <summary>Компилирует конфиг: выключенные группы пропускаются целиком, кривые правила — в <see cref="Discarded"/>.</summary>
	/// <param name="config">Конфиг из json.</param>
	/// <param name="regexMatchTimeout">Таймаут regex-матча; тестам — миллисекунды на злых паттернах.</param>
	public static RulesEngine Compile(RulesConfig config, TimeSpan? regexMatchTimeout = null)
	{
		TimeSpan timeout = regexMatchTimeout ?? _defaultRegexTimeout;
		List<CompiledRule> rules = [];
		List<DiscardedRule> discarded = [];
		int disabled = 0;

		foreach (RuleGroupConfig group in config.Groups)
		{
			if (group.Enabled == false)
			{
				disabled += group.Rules.Count;
				continue;
			}

			foreach (RuleConfig rule in group.Rules)
			{
				CompiledRule? compiled = TryCompile(group, rule, timeout, out string? reason);
				if (compiled is null)
				{
					// reason не-null при null-возврате TryCompile (все ветки отказа заполняют его).
					discarded.Add(new DiscardedRule(group.Name ?? "<без имени>", rule.Name, reason!));
				}
				else
				{
					rules.Add(compiled);
				}
			}
		}

		// ToArray, не [.. ]: спред в Core рождает синтетический тип в глобальном
		// namespace — arch-тест «типы ядра живут в пространстве ядра».
		return new RulesEngine(rules.ToArray(), disabled, discarded.ToArray());
	}

	/// <summary>Вердикт по уведомлению: первый матч или catch-all.</summary>
	/// <param name="record">Уведомление из листенера.</param>
	public RuleVerdict Decide(NotificationRecord record)
	{
		foreach (CompiledRule rule in _rules)
		{
			if (rule.Matches(record))
			{
				return rule.Verdict;
			}
		}

		return RuleVerdict.CatchAll;
	}

	// Разбор enum-подобных строк конфига: null/пусто — дефолт, кривое — null (выбраковка).
	private static MatchMode? TryParseMatchMode(string? raw) => Normalize(raw) switch
	{
		null => MatchMode.Regex,
		"regex" => MatchMode.Regex,
		"exact" => MatchMode.Exact,
		"contains" => MatchMode.Contains,
		_ => null,
	};

	private static RuleAction? TryParseAction(string? raw) => Normalize(raw) switch
	{
		null => RuleAction.Show,
		"show" => RuleAction.Show,
		"mute" => RuleAction.Mute,
		"delete" => RuleAction.Delete,
		_ => null,
	};

	private static ClickAction? TryParseClick(string? raw) => Normalize(raw) switch
	{
		null => ClickAction.Close,
		"close" => ClickAction.Close,
		"focus" => ClickAction.Focus,
		_ => null,
	};

	private static string? Normalize(string? raw)
	{
		string trimmed = (raw ?? string.Empty).Trim();
		return trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();
	}

	// null-возврат = правило выбраковано, reason заполнен причиной.
	private static CompiledRule? TryCompile(
		RuleGroupConfig group,
		RuleConfig rule,
		TimeSpan timeout,
		out string? reason)
	{
		MatchMode? mode = TryParseMatchMode(rule.Match);
		if (mode is null)
		{
			reason = $"неизвестный match: «{rule.Match}»";
			return null;
		}

		RuleAction? action = TryParseAction(rule.Action);
		if (action is null)
		{
			reason = $"неизвестный action: «{rule.Action}»";
			return null;
		}

		ClickAction? click = TryParseClick(rule.Click);
		if (click is null)
		{
			reason = $"неизвестный click: «{rule.Click}»";
			return null;
		}

		// Свои out-переменные на каждый матчер: успешный вызов сбрасывает свою
		// ошибку в null, общая затирала бы причину упавшего app.
		Func<string?, bool>? app = CompileMatcher(rule.App, mode.Value, timeout, out string? appError);
		Func<string?, bool>? title = CompileMatcher(rule.Title, mode.Value, timeout, out string? titleError);
		Func<string?, bool>? body = CompileMatcher(rule.Body, mode.Value, timeout, out string? bodyError);
		if (app is null || title is null || body is null)
		{
			reason = appError ?? titleError ?? bodyError!;
			return null;
		}

		RuleVerdict verdict = new(
			action.Value,
			action == RuleAction.Show ? ParseTtl(rule.Ttl) : null, // ttl имеет смысл только для показа
			group.Name,
			rule.Name,
			group.Color,
			rule.KillNative ?? false,
			rule.OverFullscreen ?? false,
			click.Value,
			Sound: rule.Sound,
			HideOnFullscreen: rule.HideOnFullscreen ?? false);
		reason = null;
		return new CompiledRule(app, title, body, verdict);
	}

	// Матчер одного поля: null-паттерн — wildcard; null-строка записи при
	// непустом паттерне — не совпало (матчить нечего).
	private static Func<string?, bool>? CompileMatcher(
		string? pattern,
		MatchMode mode,
		TimeSpan timeout,
		[NotNullWhen(false)] out string? error)
	{
		error = null;
		if (pattern is null)
		{
			return static _ => true;
		}

		if (mode == MatchMode.Regex)
		{
			try
			{
				Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, timeout);
				return value => MatchRegex(regex, value);
			}
			catch (ArgumentException ex)
			{
				error = $"кривой regex «{pattern}»: {ex.Message}";
				return null;
			}
		}

		return mode == MatchMode.Exact
			? value => string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase)
			: value => value is not null && value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
	}

	private static bool MatchRegex(Regex regex, string? value)
	{
		if (value is null)
		{
			return false;
		}

		try
		{
			return regex.IsMatch(value);
		}
		catch (RegexMatchTimeoutException)
		{
			return false; // таймаут ≈ «не совпало»; правило живо (док класса)
		}
	}

	// "sticky" → null (без ограничения), "5s"/"3m" — регистр не важен (грабля
	// старого rnotif: «5S» молча превращалось в дефолт), мусор/пусто → 5 с,
	// clamp 1–3600 с. Выбраковкой не является никогда.
	/// <summary>Парсер строки ttl для настройки вне правил ("30s"/"3m"/"sticky") — S7.2.</summary>
	public static TimeSpan? ParseTtl(string? raw) => ParseTtlInternal(raw);

	// "sticky" → null (без ограничения), "5s"/"3m" — регистр не важен (грабля
	// старого rnotif: «5S» молча превращалось в дефолт), мусор/пусто → 5 с,
	// clamp 1–3600 с. Выбраковкой не является никогда.
	private static TimeSpan? ParseTtlInternal(string? raw)
	{
		string value = (raw ?? string.Empty).Trim();
		if (value.Length == 0)
		{
			return DefaultShowTtl;
		}

		if (string.Equals(value, "sticky", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		if (value.Length >= 2
			&& int.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int amount)
			&& char.ToLowerInvariant(value[^1]) is 's' or 'm')
		{
			int seconds = char.ToLowerInvariant(value[^1]) == 's' ? amount : amount * 60;
			return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 3600)); // «0s» → 1 c, «9999s» → 1 ч
		}

		return DefaultShowTtl;
	}

	// Скомпилированное правило: три матчера (AND) и готовый вердикт.
	// app матчит ТОЛЬКО по AUMID — DisplayName у AppInfo в контракте 22621 нет.
	private sealed class CompiledRule(
		Func<string?, bool> app,
		Func<string?, bool> title,
		Func<string?, bool> body,
		RuleVerdict verdict)
	{
		public RuleVerdict Verdict => verdict;

		public bool Matches(NotificationRecord record) =>
			app(record.Aumid) && title(record.Title) && body(record.Body);
	}
}
