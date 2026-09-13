namespace rnotify.Core.Rules;

/// <summary>Режим сопоставления паттерна со строкой уведомления.</summary>
public enum MatchMode
{
	/// <summary>Regex: IgnoreCase | CultureInvariant, с таймаутом (дефолт).</summary>
	Regex = 0,

	/// <summary>Точное равенство, OrdinalIgnoreCase.</summary>
	Exact = 1,

	/// <summary>Подстрока, OrdinalIgnoreCase.</summary>
	Contains = 2,
}
