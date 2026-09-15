namespace rnotify.Core.Rules;

/// <summary>
/// Вердикт правил по одному уведомлению: что делать и как оформить. Несёт все
/// поля схемы v2; ttl/цвет/click/overFullscreen применит рендер Э4 — до него
/// вердикт показывается строкой в панели.
/// </summary>
/// <param name="Ttl">Время жизни своей карточки; null — sticky или действие не show.</param>
/// <param name="GroupName">Группа сработавшего правила; null — catch-all.</param>
/// <param name="RuleName">Сработавшее правило; null — catch-all.</param>
/// <param name="AccentHex">Цвет группы «#RRGGBB»; null — catch-all.</param>
/// <param name="Sound">Путь к .wav, играет при показе карточки; null — тишина.</param>
/// <param name="HideOnFullscreen">Не показывать карточку при фуллскрине foreground-окна (S8.1).</param>
public sealed record RuleVerdict(
	RuleAction Action,
	TimeSpan? Ttl,
	string? GroupName,
	string? RuleName,
	string? AccentHex,
	bool KillNative,
	bool OverFullscreen,
	ClickAction Click,
	string? Sound = null,
	bool HideOnFullscreen = false)
{
	/// <summary>Дефолт для непокрытого правилами: показать 5 с (диспетчер не глушит молча).</summary>
	public static RuleVerdict CatchAll { get; } = new(
		RuleAction.Show,
		RulesEngine.DefaultShowTtl,
		GroupName: null,
		RuleName: null,
		AccentHex: null,
		KillNative: false,
		OverFullscreen: false,
		ClickAction.Close,
		Sound: null,
		HideOnFullscreen: false);

	/// <summary>Нужно ли снести нативную копию из хранилища Центра (RemoveNotification).</summary>
	public bool RequiresNativeRemoval => Action == RuleAction.Delete || KillNative;
}
