using rnotify.Core.Listener;

namespace rnotify.Core.Tests;

/// <summary>
/// Сбор событий фида: списки Added/Removed/Failed плюс общий порядок прихода
/// (+Id/−Id в одну ленту — проверка «сначала Added, потом Removed»).
/// </summary>
internal sealed class NotificationCollector : IDisposable
{
	private readonly Lock _gate = new();
	private readonly List<NotificationRecord> _added = [];
	private readonly List<uint> _removed = [];
	private readonly List<Exception> _failed = [];
	private readonly List<string> _order = [];
	private NotificationFeed? _feed;

	public IReadOnlyList<NotificationRecord> Added
	{
		get { lock (_gate) { return [.. _added]; } }
	}

	public IReadOnlyList<uint> Removed
	{
		get { lock (_gate) { return [.. _removed]; } }
	}

	public IReadOnlyList<Exception> Failed
	{
		get { lock (_gate) { return [.. _failed]; } }
	}

	public IReadOnlyList<string> Order
	{
		get { lock (_gate) { return [.. _order]; } }
	}

	public void Attach(NotificationFeed feed)
	{
		_feed = feed;
		feed.Added += OnAdded;
		feed.Removed += OnRemoved;
		feed.SnapshotFailed += OnFailed;
	}

	public void Dispose()
	{
		if (_feed is null)
		{
			return;
		}

		_feed.Added -= OnAdded;
		_feed.Removed -= OnRemoved;
		_feed.SnapshotFailed -= OnFailed;
	}

	private void OnAdded(object? sender, NotificationAddedEventArgs e)
	{
		lock (_gate)
		{
			_added.Add(e.Record);
			_order.Add($"+{e.Record.Id}");
		}
	}

	private void OnRemoved(object? sender, NotificationRemovedEventArgs e)
	{
		lock (_gate)
		{
			_removed.Add(e.Id);
			_order.Add($"-{e.Id}");
		}
	}

	private void OnFailed(object? sender, NotificationFailedEventArgs e)
	{
		lock (_gate)
		{
			_failed.Add(e.Error);
		}
	}
}
