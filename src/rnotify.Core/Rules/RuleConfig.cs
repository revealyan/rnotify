namespace rnotify.Core.Rules;

/// <summary>
/// Правило в json-форме (v2). Все поля опциональны; enum-подобные значения —
/// строками, разбор на компиляции: кривое значение = выбраковка одного правила
/// с отчётом, а не JsonException на весь файл.
/// </summary>
public sealed class RuleConfig
{
	/// <summary>Имя правила (панель, отчёты выбраковки).</summary>
	public string? Name { get; set; }

	/// <summary>Матчер отправителя; матчит ТОЛЬКО по AUMID (DisplayName в контракте 22621 нет).</summary>
	public string? App { get; set; }

	/// <summary>Матчер заголовка (первый текстовый элемент тоста).</summary>
	public string? Title { get; set; }

	/// <summary>Матчер тела (остальные текстовые элементы, join «\n»).</summary>
	public string? Body { get; set; }

	/// <summary>Режим сопоставления: regex | exact | contains (дефолт regex).</summary>
	public string? Match { get; set; }

	/// <summary>Действие: show | mute | delete (дефолт show).</summary>
	public string? Action { get; set; }

	/// <summary>Время жизни своей карточки: «5s» | «3m» | «sticky» (смысл — только для show).</summary>
	public string? Ttl { get; set; }

	/// <summary>Снести нативную копию из Центра при матче (лечение reminder-утечек).</summary>
	public bool? KillNative { get; set; }

	/// <summary>Показывать поверх фуллскрина (применяет рендер Э4).</summary>
	public bool? OverFullscreen { get; set; }

	/// <summary>Клик по карточке: close | focus (дефолт close).</summary>
	public string? Click { get; set; }
}
