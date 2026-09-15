namespace rnotify;

/// <summary>
/// Строки UI (S6.2): en-нейтральный Strings.resx + ru-сателлит Strings.ru.resx.
/// Класс-обёртка написан руками (dotnet build не запускает VS-генератор resx,
/// CI собирает без Visual Studio) — это то, что сгенерировал бы
/// PublicResXFileCodeGenerator. Не редактировать значения здесь — только
/// в resx; сюда добавлять свойства под новые ключи.
/// </summary>
public static class Strings
{
	private static global::System.Resources.ResourceManager? _resourceMan;
	private static global::System.Globalization.CultureInfo? _resourceCulture;

	private static global::System.Resources.ResourceManager ResourceManager
	{
		get
		{
			if (_resourceMan is null)
			{
				global::System.Resources.ResourceManager manager =
					new("rnotify.Strings", typeof(Strings).Assembly);
				_resourceMan = manager;
			}

			return _resourceMan;
		}
	}

	/// <summary>Культура диспетчеризации строк; null — CurrentUICulture.</summary>
	public static global::System.Globalization.CultureInfo? Culture
	{
		get => _resourceCulture;
		set => _resourceCulture = value;
	}

	private static string Get(string key, string fallback) => ResourceManager.GetString(key, _resourceCulture) ?? fallback;

	/// <summary>Меню трея: открыть панель.</summary>
	public static string TrayMenuPanel => Get("TrayMenuPanel", "Panel");

	/// <summary>Меню трея: панель истории.</summary>
	public static string TrayMenuHistory => Get("TrayMenuHistory", "History");

	/// <summary>Меню трея: автозапуск.</summary>
	public static string TrayMenuAutostart => Get("TrayMenuAutostart", "Autostart");

	/// <summary>Плашка догоняющих: осталось одно.</summary>
	public static string CatchUpRemainingOne => Get("CatchUpRemainingOne", "catch-up notification left");

	/// <summary>Плашка догоняющих: осталось много ({0}).</summary>
	public static string CatchUpRemainingMany => Get("CatchUpRemainingMany", "catch-up notifications left: {0}");

	/// <summary>Меню трея: пропустить поток догоняющих.</summary>
	public static string SkipCatchUp => Get("SkipCatchUp", "Skip catch-up");

	/// <summary>Меню трея: выход.</summary>
	public static string TrayMenuExit => Get("TrayMenuExit", "Exit");

	/// <summary>Заголовок секции панели.</summary>
	public static string PanelHeader => Get("PanelHeader", "Package identity");

	/// <summary>Кнопка копирования строк панели.</summary>
	public static string CopyAll => Get("CopyAll", "Copy all");

	/// <summary>Формат «скопировано N» (форматный аргумент {0}).</summary>
	public static string CopiedFormat => Get("CopiedFormat", "Copied ({0})");

	/// <summary>Префикс ошибки копирования.</summary>
	public static string CopyFailedPrefix => Get("CopyFailedPrefix", "Failed: ");

	/// <summary>Ключ строки: режим запуска.</summary>
	public static string RowMode => Get("RowMode", "Mode");

	/// <summary>Ключ строки: сверка identity манифеста и кода.</summary>
	public static string RowIdentityMatch => Get("RowIdentityMatch", "Identity match");

	/// <summary>Ключ строки: настройки.</summary>
	public static string RowSettings => Get("RowSettings", "Settings");

	/// <summary>Ключ строки: трей.</summary>
	public static string RowTray => Get("RowTray", "Tray");

	/// <summary>Ключ строки: правила.</summary>
	public static string RowRules => Get("RowRules", "Rules");

	/// <summary>Ключ строки: листенер.</summary>
	public static string RowListener => Get("RowListener", "Listener");

	/// <summary>Ключ строки: снапшот.</summary>
	public static string RowSnapshot => Get("RowSnapshot", "Snapshot");

	/// <summary>Ключ строки: стек карточек.</summary>
	public static string RowStack => Get("RowStack", "Stack");


	/// <summary>История: RNotify — history.</summary>
	public static string HistoryTitle => Get("HistoryTitle", "RNotify — history");
	/// <summary>История: shown only.</summary>
	public static string HistoryOnlyShown => Get("HistoryOnlyShown", "shown only");
	/// <summary>История: Refresh.</summary>
	public static string HistoryRefresh => Get("HistoryRefresh", "Refresh");
	/// <summary>История: search….</summary>
	public static string HistorySearchHint => Get("HistorySearchHint", "search…");
	/// <summary>История: {0} of {1}.</summary>
	public static string HistoryCountFormat => Get("HistoryCountFormat", "{0} of {1}");
	/// <summary>История: Show again as card.</summary>
	public static string HistoryActionReshow => Get("HistoryActionReshow", "Show again as card");
	/// <summary>История: Copy text.</summary>
	public static string HistoryActionCopy => Get("HistoryActionCopy", "Copy text");
	/// <summary>История: Sender.</summary>
	public static string HistoryCopySender => Get("HistoryCopySender", "Sender");
	/// <summary>История: Title.</summary>
	public static string HistoryCopyTitle => Get("HistoryCopyTitle", "Title");
	/// <summary>История: Body.</summary>
	public static string HistoryCopyBody => Get("HistoryCopyBody", "Body");


	/// <summary>Редактор правил: Rules….</summary>
	public static string TrayMenuRules => Get("TrayMenuRules", "Rules…");
	/// <summary>Редактор правил: RNotify — rules.</summary>
	public static string RulesTitle => Get("RulesTitle", "RNotify — rules");
	/// <summary>Редактор правил: + group.</summary>
	public static string RulesAddGroup => Get("RulesAddGroup", "+ group");
	/// <summary>Редактор правил: − group.</summary>
	public static string RulesRemoveGroup => Get("RulesRemoveGroup", "− group");
	/// <summary>Редактор правил: + rule.</summary>
	public static string RulesAddRule => Get("RulesAddRule", "+ rule");
	/// <summary>Редактор правил: − rule.</summary>
	public static string RulesRemoveRule => Get("RulesRemoveRule", "− rule");
	/// <summary>Редактор правил: Save.</summary>
	public static string RulesSave => Get("RulesSave", "Save");
	/// <summary>Редактор правил: Cancel.</summary>
	public static string RulesCancel => Get("RulesCancel", "Cancel");
	/// <summary>Редактор правил: saved — engine reloaded.</summary>
	public static string RulesSaved => Get("RulesSaved", "saved — engine reloaded");
	/// <summary>Редактор правил: on.</summary>
	public static string RulesGroupEnabled => Get("RulesGroupEnabled", "on");
	/// <summary>Редактор правил: (no group).</summary>
	public static string RulesNoGroup => Get("RulesNoGroup", "(no group)");
	/// <summary>Редактор правил: (no rule).</summary>
	public static string RulesNoRule => Get("RulesNoRule", "(no rule)");
	/// <summary>Редактор правил: (default: regex).</summary>
	public static string RulesMatchAuto => Get("RulesMatchAuto", "(default: regex)");

	/// <summary>Ключ строки: автозапуск.</summary>
	public static string RowAutostart => Get("RowAutostart", "Autostart");
}
