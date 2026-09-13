namespace rnotify.Core.Listener;

/// <summary>Аргументы события SnapshotFailed: ошибка чтения снапшота.</summary>
public sealed class NotificationFailedEventArgs(Exception error) : EventArgs
{
	/// <summary>Ошибка чтения снапшота (HRESULT листенера и т.п.).</summary>
	public Exception Error { get; } = error;
}
