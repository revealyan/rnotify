using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace rnotify.Core.Listener;

/// <summary>
/// Прод-источник: UserNotificationListener.Current, контракт 22621. Единственная
/// реализация <see cref="INotificationSource"/> с WinRT; юнит-тесты её не трогают
/// (WinRT-типы без публичных конструкторов), корректность подтверждается живым
/// прогоном пакета.
/// </summary>
public sealed class UserNotificationSource : INotificationSource
{
	private readonly UserNotificationListener _listener = UserNotificationListener.Current;
	private Action? _onChanged;

	/// <inheritdoc />
	public async Task<IReadOnlyList<NotificationRecord>> GetSnapshotAsync()
	{
		// ToArray вместо collection-выражения [.. ]: спред генерирует синтетический
		// тип в глобальном namespace — arch-тест «типы ядра в пространстве ядра».
		IReadOnlyList<UserNotification> notifications =
			await _listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask().ConfigureAwait(false);
		return notifications.Select(Map).ToArray();
	}

	/// <inheritdoc />
	public async Task<NotificationAccessStatus> RequestAccessAsync() =>
		Map(await _listener.RequestAccessAsync().AsTask().ConfigureAwait(false));

	/// <inheritdoc />
	public void StartListening(Action onChanged)
	{
		_onChanged = onChanged;
		_listener.NotificationChanged += OnNotificationChanged;
	}

	/// <inheritdoc />
	public void StopListening()
	{
		_listener.NotificationChanged -= OnNotificationChanged;
		_onChanged = null;
	}

	/// <inheritdoc />
	public void RemoveNotification(uint notificationId) => _listener.RemoveNotification(notificationId);

	// Обычный void-хендлер (async void запрещён): мост без полезной нагрузки.
	private void OnNotificationChanged(UserNotificationListener sender, object args) => _onChanged?.Invoke();

	// AppInfo листенера (Windows.UI.Notifications.AppInfo) несёт только AUMID —
	// отображаемое имя отправителя в запись не попадает (разрешение имён — Э3/Э4).
	private static NotificationRecord Map(UserNotification notification)
	{
		(string title, string body) = ToastContentReader.Read(notification.Notification);
		return new NotificationRecord(
			notification.Id,
			notification.AppInfo?.AppUserModelId,
			title,
			body,
			notification.CreationTime);
	}

	private static NotificationAccessStatus Map(UserNotificationListenerAccessStatus status) => status switch
	{
		UserNotificationListenerAccessStatus.Allowed => NotificationAccessStatus.Allowed,
		UserNotificationListenerAccessStatus.Denied => NotificationAccessStatus.Denied,
		_ => NotificationAccessStatus.Unspecified,
	};
}
