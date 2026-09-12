namespace rnotify.Core;

/// <summary>
/// Идентичность продукта — единый источник истины для мьютекса, логов и сверки
/// с манифестом пакета. Значения зеркалят docs/store-identity.md (Partner
/// Center); при смене identity менять в двух местах синхронно.
/// </summary>
public static class ProductIdentity
{
	/// <summary>Имя пакета (Package/Identity/Name, Partner Center).</summary>
	public const string Name = "revealyan.RNotify";

	/// <summary>Издатель (Publisher); он же Subject дев-сертификата — совпадение обязательно.</summary>
	public const string Publisher = "CN=6F4182DB-0063-4273-87C8-59E15AFFCBCC";

	/// <summary>Имя мьютекса single-instance: диспетчер уведомлений всегда один.</summary>
	public const string MutexName = @"Local\rnotify-single-instance";
}
