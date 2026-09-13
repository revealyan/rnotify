namespace rnotify.Core.Rules;

/// <summary>Группа правил в json-форме (v2): переключатель и общий акцент.</summary>
public sealed class RuleGroupConfig
{
	/// <summary>Имя группы (панель, отчёты выбраковки).</summary>
	public string? Name { get; set; }

	/// <summary>Включённость; null/отсутствие = включена (bool без значения в json = false — асимметрия осознанная).</summary>
	public bool? Enabled { get; set; }

	/// <summary>Акцент группы «#RRGGBB»; валидация — за рендером Э4.</summary>
	public string? Color { get; set; }

	/// <summary>Правила группы по порядку; побеждает первый матч.</summary>
	public IList<RuleConfig> Rules { get; set; } = [];
}
