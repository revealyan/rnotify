namespace rnotify.Core.Listener;

/// <summary>
/// Единственная точка, где в ядро входит UserNotificationListener: прод-источник
/// (<see cref="UserNotificationSource"/>) оборачивает WinRT, тесты подставляют
/// фейк с записями и ручным сигналом изменений.
/// </summary>
public interface INotificationSource
{
	/// <summary>Снапшот хранилища (NotificationKinds.Toast), уже домапленный в записи.</summary>
	public Task<IReadOnlyList<NotificationRecord>> GetSnapshotAsync();

	/// <summary>Consent-запрос системы (RequestAccessAsync).</summary>
	public Task<NotificationAccessStatus> RequestAccessAsync();

	/// <summary>
	/// Включить сигнал изменений хранилища. Вызывается только после успешного
	/// consent — порядок «consent → подписка» валидирован спайком S5.2
	/// (обратный не проверялся, отступать незачем).
	/// </summary>
	/// <param name="onChanged">Колбек «хранилище изменилось»; летит пачками, аргументов нет.</param>
	public void StartListening(Action onChanged);

	/// <summary>Выключить сигнал изменений (отписка от WinRT-события).</summary>
	public void StopListening();
}
