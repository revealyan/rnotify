namespace rnotify.Core.Listener;

/// <summary>Аргументы события Added: появившееся уведомление.</summary>
public sealed class NotificationAddedEventArgs(NotificationRecord record) : EventArgs
{
	/// <summary>Уведомление, которого не было в прошлом снапшоте.</summary>
	public NotificationRecord Record { get; } = record;
}
