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

	/// <summary>Меню трея: автозапуск.</summary>
	public static string TrayMenuAutostart => Get("TrayMenuAutostart", "Autostart");

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

	/// <summary>Ключ строки: автозапуск.</summary>
	public static string RowAutostart => Get("RowAutostart", "Autostart");
}
