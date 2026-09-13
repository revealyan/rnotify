namespace rnotify.Core.Listener;

/// <summary>Итог consent-запроса листенера: проекция WinRT-статусов плюс собственный TimedOut.</summary>
public enum NotificationAccessStatus
{
	/// <summary>Доступ выдан (WinRT Allowed).</summary>
	Allowed = 0,

	/// <summary>Отказ (WinRT Denied).</summary>
	Denied = 1,

	/// <summary>Неопределённый ответ системы (WinRT Unspecified).</summary>
	Unspecified = 2,

	/// <summary>Диалог не отвечен за таймаут: фид неактивен, поможет перезапуск приложения.</summary>
	TimedOut = 3,
}
