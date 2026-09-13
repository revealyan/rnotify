using rnotify.Core.Listener;

namespace rnotify.Core.Tests;

/// <summary>
/// Фейк источника: снапшот руками, consent — TaskCompletionSource, сигнал
/// изменений — вручную. WinRT в тестах не нужен. Детерминизм: GetSnapshotAsync
/// завершается синхронно, поэтому TriggerChanged прогоняет дифф фида до конца
/// до возврата в тест.
/// </summary>
internal sealed class FakeNotificationSource : INotificationSource
{
	// RunContinuationsAsynchronously: иначе синхронные продолжения
	// WhenAny образуют лестницу и могут дедлоковать семафор диффа.
	private readonly TaskCompletionSource<NotificationAccessStatus> _consent =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly List<uint> _removeCalls = [];

	private IReadOnlyList<NotificationRecord> _snapshot = [];
	private Action? _onChanged;
	private int _snapshotCalls;

	public int SnapshotCalls => _snapshotCalls;

	/// <summary>Id, по которым звали RemoveNotification, по порядку.</summary>
	public IReadOnlyList<uint> RemoveCalls => _removeCalls;

	/// <summary>Когда установлен — GetSnapshotAsync падает этим исключением (последующие диффы).</summary>
	public Exception? SnapshotError { get; set; }

	public void CompleteConsent(NotificationAccessStatus status) => _consent.SetResult(status);

	public void SetSnapshot(params NotificationRecord[] records) => _snapshot = records;

	public void TriggerChanged() => _onChanged?.Invoke();

	/// <inheritdoc />
	public Task<IReadOnlyList<NotificationRecord>> GetSnapshotAsync()
	{
		Interlocked.Increment(ref _snapshotCalls);
		return SnapshotError is { } error
			? Task.FromException<IReadOnlyList<NotificationRecord>>(error)
			: Task.FromResult(_snapshot);
	}

	/// <inheritdoc />
	public Task<NotificationAccessStatus> RequestAccessAsync() => _consent.Task;

	/// <inheritdoc />
	public void StartListening(Action onChanged) => _onChanged = onChanged;

	/// <inheritdoc />
	public void StopListening() => _onChanged = null;

	/// <inheritdoc />
	public void RemoveNotification(uint notificationId) => _removeCalls.Add(notificationId);
}
