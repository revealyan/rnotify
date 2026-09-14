using rnotify.Core.Listener;
using Xunit;

namespace rnotify.Core.Tests;

/// <summary>
/// Floor «что уже обработано»: пары Id+время (рециркуляция Id — не дубль),
/// персистентность, чистка по возрасту, битый файл = пустой.
/// </summary>
public sealed class NotificationFloorTests
{
	[Fact]
	public void Отмеченное_видено_перезаписка_персистентна()
	{
		using TempDir dir = new();
		string path = dir.PathFor("seen.json");
		DateTimeOffset raised = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
		NotificationFloor floor = new(path);
		NotificationRecord record = new(77, "app", "t", "b", raised);

		Assert.False(floor.WasSeen(record)); // свежее — не видели
		floor.MarkSeen(record);

		Assert.True(floor.WasSeen(record));
		// Второй инстанс читает диск, не память.
		Assert.True(new NotificationFloor(path).WasSeen(record));
	}

	[Fact]
	public void Тот_же_Id_другое_время_не_видели()
	{
		using TempDir dir = new();
		NotificationFloor floor = new(dir.PathFor("seen.json"));
		DateTimeOffset first = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
		DateTimeOffset second = new(2026, 9, 14, 13, 0, 0, TimeSpan.Zero);
		floor.MarkSeen(new NotificationRecord(5, "app", "t", "b", first));

		// Хранилище переиспользует Id после сноса — это ДРУГОЕ уведомление.
		Assert.False(floor.WasSeen(new NotificationRecord(5, "app", "t", "b", second)));
	}

	[Fact]
	public void Битый_файл_пустой_floor()
	{
		using TempDir dir = new();
		string path = dir.PathFor("seen.json");
		File.WriteAllText(path, "{ кривой json");
		DateTimeOffset raised = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

		NotificationFloor floor = new(path);

		Assert.False(floor.WasSeen(9, raised)); // пустой, не исключение
		floor.MarkSeen(9, raised);
		Assert.True(new NotificationFloor(path).WasSeen(9, raised)); // файл починился записью
	}

	[Fact]
	public void Древние_записи_вычищаются_при_перезаписи()
	{
		using TempDir dir = new();
		string path = dir.PathFor("seen.json");
		// Файл с записью недельной давности (unix-время руками).
		long old = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(30)).ToUnixTimeSeconds();
		File.WriteAllText(path, $$"""{ "1|638000000000000000": {{old}} }""");

		NotificationFloor floor = new(path);
		DateTimeOffset now = DateTimeOffset.UtcNow;
		floor.MarkSeen(2, now);

		string content = File.ReadAllText(path);
		Assert.DoesNotContain("638000000000000000", content, StringComparison.Ordinal); // старьё выпилено
		Assert.True(new NotificationFloor(path).WasSeen(2, now));
	}
}
