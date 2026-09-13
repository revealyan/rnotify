namespace rnotify.Core.Listener;

/// <summary>Аргументы события Removed: Id исчезнувшего уведомления.</summary>
public sealed class NotificationRemovedEventArgs(uint id) : EventArgs
{
	/// <summary>Id уведомления, исчезнувшего из хранилища.</summary>
	public uint Id { get; } = id;
}
