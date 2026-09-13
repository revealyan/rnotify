using rnotify.Core.Listener;
using Xunit;

namespace rnotify.Core.Tests;

/// <summary>
/// Юнит-тесты фида на фейке источника: дифф по Id, базлайн-backlog, consent-
/// ветвления, пачки сигналов, устойчивость к падению снапшота. Детерминизм без
/// слипов — см. <see cref="FakeNotificationSource"/>.
/// </summary>
public sealed class NotificationFeedTests
{
	private static NotificationRecord Rec(uint id, string title = "титул", string body = "тело") =>
		new(id, "aumid-тест", title, body, DateTimeOffset.UnixEpoch);

	private static async Task<(NotificationFeed Feed, FakeNotificationSource Fake)> StartAllowedAsync()
	{
		FakeNotificationSource fake = new();
		fake.CompleteConsent(NotificationAccessStatus.Allowed);
		NotificationFeed feed = new(fake, consentTimeout: TimeSpan.FromSeconds(5));
		ListenerStartResult start = await feed.StartAsync().ConfigureAwait(false);
		Assert.Equal(NotificationAccessStatus.Allowed, start.Status);
		return (feed, fake);
	}

	[Fact]
	public async Task Базлайн_не_порождает_событий()
	{
		FakeNotificationSource fake = new();
		fake.SetSnapshot(Rec(1), Rec(2));
		fake.CompleteConsent(NotificationAccessStatus.Allowed);
		using NotificationFeed feed = new(fake, consentTimeout: TimeSpan.FromSeconds(5));
		using NotificationCollector collector = new();
		collector.Attach(feed);

		ListenerStartResult start = await feed.StartAsync();

		Assert.Equal(NotificationAccessStatus.Allowed, start.Status);
		Assert.Equal(2, start.BaselineCount);
		Assert.Empty(collector.Added);
		Assert.Empty(collector.Removed);
	}

	[Fact]
	public async Task Новое_уведомление_порождает_Added_целиком()
	{
		(NotificationFeed feed, FakeNotificationSource fake) = await StartAllowedAsync();
		using NotificationCollector collector = new();
		collector.Attach(feed);

		NotificationRecord sent = Rec(7, "Заголовок", "Строка 1|Строка 2");
		fake.SetSnapshot(sent);
		fake.TriggerChanged();

		NotificationRecord received = Assert.Single(collector.Added);
		Assert.Equal(sent, received);
		Assert.Empty(collector.Removed);
	}

	[Fact]
	public async Task Исчезновение_порождает_Removed()
	{
		FakeNotificationSource fake = new();
		fake.SetSnapshot(Rec(7));
		fake.CompleteConsent(NotificationAccessStatus.Allowed);
		using NotificationFeed feed = new(fake, consentTimeout: TimeSpan.FromSeconds(5));
		using NotificationCollector collector = new();
		collector.Attach(feed);
		await feed.StartAsync();

		fake.SetSnapshot();
		fake.TriggerChanged();

		Assert.Equal(7u, Assert.Single(collector.Removed));
		Assert.Empty(collector.Added);
	}

	[Fact]
	public async Task Пачка_Changed_диффается_без_дублей()
	{
		(NotificationFeed feed, FakeNotificationSource fake) = await StartAllowedAsync();
		using NotificationCollector collector = new();
		collector.Attach(feed);

		fake.SetSnapshot(Rec(1));
		fake.TriggerChanged();
		fake.TriggerChanged();
		fake.TriggerChanged();

		Assert.Single(collector.Added);
		Assert.Empty(collector.Removed);
		Assert.Equal(4, fake.SnapshotCalls); // базлайн + по диффу на каждый сигнал
	}

	[Fact]
	public async Task Плюс_и_минус_одним_диффом_сначала_Added()
	{
		FakeNotificationSource fake = new();
		fake.SetSnapshot(Rec(1), Rec(2));
		fake.CompleteConsent(NotificationAccessStatus.Allowed);
		using NotificationFeed feed = new(fake, consentTimeout: TimeSpan.FromSeconds(5));
		using NotificationCollector collector = new();
		collector.Attach(feed);
		await feed.StartAsync();

		fake.SetSnapshot(Rec(2), Rec(3));
		fake.TriggerChanged();

		Assert.Equal(3u, Assert.Single(collector.Added).Id);
		Assert.Equal(1u, Assert.Single(collector.Removed));
		Assert.Equal(["+3", "-1"], collector.Order);
	}

	[Fact]
	public async Task Denied_не_подписывает_источник()
	{
		FakeNotificationSource fake = new();
		fake.CompleteConsent(NotificationAccessStatus.Denied);
		using NotificationFeed feed = new(fake, consentTimeout: TimeSpan.FromSeconds(5));
		using NotificationCollector collector = new();
		collector.Attach(feed);

		ListenerStartResult start = await feed.StartAsync();

		Assert.Equal(NotificationAccessStatus.Denied, start.Status);
		Assert.Equal(0, start.BaselineCount);

		fake.SetSnapshot(Rec(1));
		fake.TriggerChanged();

		Assert.Equal(0, fake.SnapshotCalls);
		Assert.Empty(collector.Added);
	}

	[Fact]
	public async Task Молчаливый_consent_дает_TimedOut()
	{
		FakeNotificationSource fake = new();
		using NotificationFeed feed = new(fake, consentTimeout: TimeSpan.FromMilliseconds(50));

		ListenerStartResult start = await feed.StartAsync();

		Assert.Equal(NotificationAccessStatus.TimedOut, start.Status);
		Assert.Equal(0, fake.SnapshotCalls);
	}

	[Fact]
	public async Task Повторный_старт_кидает_InvalidOperationException()
	{
		(NotificationFeed feed, FakeNotificationSource _) = await StartAllowedAsync();

		InvalidOperationException thrown =
			await Assert.ThrowsAsync<InvalidOperationException>(() => feed.StartAsync());

		Assert.Contains("однократный", thrown.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Dispose_останавливает_диффы()
	{
		(NotificationFeed feed, FakeNotificationSource fake) = await StartAllowedAsync();
		using NotificationCollector collector = new();
		collector.Attach(feed);

		feed.Dispose();
		fake.SetSnapshot(Rec(1));
		fake.TriggerChanged();

		Assert.Equal(1, fake.SnapshotCalls); // только базлайн
		Assert.Empty(collector.Added);
	}

	[Fact]
	public async Task Падение_снапшота_дает_SnapshotFailed_и_фид_жив()
	{
		(NotificationFeed feed, FakeNotificationSource fake) = await StartAllowedAsync();
		using NotificationCollector collector = new();
		collector.Attach(feed);

		InvalidOperationException error = new("бой снапшота");
		fake.SnapshotError = error;
		fake.TriggerChanged();

		Assert.Same(error, Assert.Single(collector.Failed));

		fake.SnapshotError = null;
		fake.SetSnapshot(Rec(5));
		fake.TriggerChanged();

		Assert.Equal(5u, Assert.Single(collector.Added).Id);
	}
}
