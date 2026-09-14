namespace rnotify.Core;

/// <summary>
/// Идентичность продукта — единый источник истины для мьютекса, логов и сверки
/// с манифестом пакета. Значения зеркалят docs/store-identity.md (Partner
/// Center); при смене identity менять в двух местах синхронно.
/// </summary>
/// <remarks>
/// Намеренно static readonly, а не const: константы компилятор инлайнит в
/// место использования, и ссылка приложения на ядро исчезает из метаданных
/// (arch-тест «приложение ссылается на ядро» ловит пустоту).
/// </remarks>
public static class ProductIdentity
{
	/// <summary>Имя пакета (Package/Identity/Name, Partner Center).</summary>
	public static readonly string Name = "revealyan.RNotify";

	/// <summary>Издатель (Publisher); он же Subject дев-сертификата — совпадение обязательно.</summary>
	public static readonly string Publisher = "CN=6F4182DB-0063-4273-87C8-59E15AFFCBCC";

	/// <summary>Имя мьютекса single-instance: диспетчер уведомлений всегда один.</summary>
	public static readonly string MutexName = @"Local\rnotify-single-instance";

	/// <summary>Имя EventWaitHandle «покажи панель»: второй инстанс сигналит живому (S6.1).</summary>
	public static readonly string ShowPanelEventName = @"Local\rnotify-show-panel";
}
