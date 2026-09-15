namespace rnotify.Core.Suppression;

/// <summary>
/// Шов над кустом реестра уведомлений HKCU
/// Software\Microsoft\Windows\CurrentVersion\Notifications\Settings: имя
/// подключа = AUMID отправителя, значения DWORD — тумблеры показа (глобальные
/// лежат в корне куста). Прод-реализация — <see cref="RegistryNotificationSettings"/>,
/// тесты — in-memory фейк. Записи из packaged попадают в реальный улей парой
/// манифеста unvirtualizedResources + RegistryWriteVirtualization=disabled
/// (канон §10c) — без неё ключи уходят в Helium\User.dat, WpnService их не видит.
/// </summary>
public interface INotificationSettingsRegistry
{
	/// <summary>Имена подключей-отправителей (AUMID); пусто — ветки нет или отправителей нет.</summary>
	public IReadOnlyList<string> GetSenderKeys();

	/// <summary>Значение DWORD в корне куста (глобальные тумблеры); null — значения нет.</summary>
	public int? GetRootDword(string valueName);

	/// <summary>Записать DWORD в корень куста.</summary>
	public void SetRootDword(string valueName, int value);

	/// <summary>Удалить значение из корня; отсутствующее — тихо.</summary>
	public void DeleteRootValue(string valueName);

	/// <summary>Значение DWORD у отправителя; null — подключа или значения нет.</summary>
	public int? GetSenderDword(string senderKey, string valueName);

	/// <summary>Записать DWORD отправителю (подключ создаётся при нужде).</summary>
	public void SetSenderDword(string senderKey, string valueName, int value);

	/// <summary>Удалить значение у отправителя; отсутствующее — тихо.</summary>
	public void DeleteSenderValue(string senderKey, string valueName);

	/// <summary>Строковое значение у отправителя (SoundFile); null — нет.</summary>
	public string? GetSenderString(string senderKey, string valueName);

	/// <summary>Записать строковое значение отправителю.</summary>
	public void SetSenderString(string senderKey, string valueName, string value);
}
