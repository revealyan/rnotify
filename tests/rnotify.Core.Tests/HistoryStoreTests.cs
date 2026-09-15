using rnotify.Core.History;
using Xunit;

namespace rnotify.Core.Tests;

/// <summary>
/// Хранилище истории: порядок, лимит-чистка (свежайшие остаются),
/// персистентность, битый файл = пустая история, лимит 0 = выключено.
/// </summary>
public sealed class HistoryStoreTests
{
	[Fact]
	public void Добавление_порядок_и_снимок_свежайшие_вперед()
	{
		using TempDir dir = new();
		HistoryStore store = new(100, dir.PathFor("history.json"));
		store.Add(new HistoryEntry(100, "a", "A", "t1", "b1", "show"));
		store.Add(new HistoryEntry(200, "a", "A", "t2", "b2", "mute"));

		IReadOnlyList<HistoryEntry> snapshot = store.SnapshotNewestFirst();

		Assert.Equal(2, snapshot.Count);
		Assert.Equal("t2", snapshot[0].Title); // свежая первой
		Assert.Equal("t1", snapshot[1].Title);
	}

	[Fact]
	public void Лимит_оставляет_свежайшие_перезапуск_читает_диск()
	{
		using TempDir dir = new();
		string path = dir.PathFor("history.json");
		HistoryStore store = new(3, path);
		foreach (long stamp in new long[] { 1, 2, 3, 4, 5 })
		{
			store.Add(new HistoryEntry(stamp, "a", "A", $"t{stamp}", "", "show"));
		}

		IReadOnlyList<HistoryEntry> snapshot = store.SnapshotNewestFirst();
		Assert.Equal([5L, 4L, 3L], snapshot.Select(e => e.RaisedUnix).ToArray()); // старьё (1,2) выпилено
		Assert.Equal([5L, 4L, 3L], new HistoryStore(3, path).SnapshotNewestFirst().Select(e => e.RaisedUnix).ToArray());
	}

	[Fact]
	public void Лимит_ноль_история_выключена()
	{
		using TempDir dir = new();
		string path = dir.PathFor("history.json");
		HistoryStore store = new(0, path);

		store.Add(new HistoryEntry(1, "a", "A", "t", "", "show"));

		Assert.Empty(store.SnapshotNewestFirst());
		Assert.False(File.Exists(path));
	}

	[Fact]
	public void Битый_файл_пустая_история_запись_чинит()
	{
		using TempDir dir = new();
		string path = dir.PathFor("history.json");
		File.WriteAllText(path, "{ кривой json");

		HistoryStore store = new(10, path);
		Assert.Empty(store.SnapshotNewestFirst()); // не исключение

		store.Add(new HistoryEntry(7, "a", "A", "t", "", "show"));
		Assert.Single(new HistoryStore(10, path).SnapshotNewestFirst()); // файл перезаписан валидно
	}
}
