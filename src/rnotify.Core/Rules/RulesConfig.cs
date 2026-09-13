namespace rnotify.Core.Rules;

/// <summary>Корень rules.json (v2): группы с правилами.</summary>
public sealed class RulesConfig
{
	/// <summary>Группы по порядку; порядок файла = приоритет.</summary>
	public IList<RuleGroupConfig> Groups { get; set; } = [];
}
