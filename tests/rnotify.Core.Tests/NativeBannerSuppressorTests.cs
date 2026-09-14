using rnotify.Core.Suppression;
using Xunit;

namespace rnotify.Core.Tests;

/// <summary>
/// Супрессор Э1: применение формулы, возврат priors, маркер-крах-безопасность
/// (авторепейр), blanket новых отправителей, откат при сбое посреди записей.
/// </summary>
public sealed class NativeBannerSuppressorTests : IDisposable
{
	private readonly TempDir _dir = new();

	private string MarkerPath => _dir.PathFor("suppression.json");

	[Fact]
	public void Apply_гасит_глобальный_тумблер_и_всех_отправителей_и_пишет_маркер()
	{
		FakeNotificationSettings registry = new();
		registry.SeedSender("app.a", null);
		registry.SeedSender("app.b", 1);
		using NativeBannerSuppressor suppressor = new(registry, MarkerPath);

		suppressor.Apply();

		Assert.Equal(0, registry.GetRootDword(NativeBannerSuppressor.GlobalToastsEnabledName));
		Assert.Equal(0, registry.GetSenderDword("app.a", NativeBannerSuppressor.ShowBannerName));
		Assert.Equal(0, registry.GetSenderDword("app.b", NativeBannerSuppressor.ShowBannerName));
		Assert.True(File.Exists(MarkerPath));
	}

	[Fact]
	public void Restore_возвращает_прошлое_былое_пишем_созданное_удаляем_маркер_стирается()
	{
		FakeNotificationSettings registry = new();
		registry.SeedSender("app.a", null); // не было значения
		registry.SeedSender("app.b", 1);     // было 1
		using NativeBannerSuppressor suppressor = new(registry, MarkerPath);
		suppressor.Apply();

		suppressor.Restore();

		// Глобального не было → явная единица (§10a: удаление не будит шелл).
		Assert.Equal(1, registry.GetRootDword(NativeBannerSuppressor.GlobalToastsEnabledName));
		Assert.Null(registry.GetSenderDword("app.a", NativeBannerSuppressor.ShowBannerName));
		Assert.Equal(1, registry.GetSenderDword("app.b", NativeBannerSuppressor.ShowBannerName));
		Assert.False(File.Exists(MarkerPath));
	}

	[Fact]
	public void Restore_сохраняет_пользовательский_глобальный_ноль()
	{
		FakeNotificationSettings registry = new();
		registry.SeedRoot(NativeBannerSuppressor.GlobalToastsEnabledName, 0); // юзер сам выключил баннеры
		using NativeBannerSuppressor suppressor = new(registry, MarkerPath);
		suppressor.Apply();

		suppressor.Restore();

		Assert.Equal(0, registry.GetRootDword(NativeBannerSuppressor.GlobalToastsEnabledName));
	}

	[Fact]
	public void Apply_чинит_реестр_по_маркеру_прошлой_сессии_и_не_теряет_priors()
	{
		// Сессия 1: применяла формулу и рухнула без Restore — маркер остался на диске.
		// Dispose не зовём (страховочная сетка вернула бы реестр): подавитель
		// просто бросается, финализатора нет.
		FakeNotificationSettings crashed = new();
		crashed.SeedSender("app.b", 1);
		NativeBannerSuppressor first = new(crashed, MarkerPath);
		first.Apply();

		// Сессия 2: свежий реестр в состоянии «как после краха» (формула применена),
		// тот же маркер на диске.
		FakeNotificationSettings residue = new();
		residue.SeedRoot(NativeBannerSuppressor.GlobalToastsEnabledName, 0);
		residue.SeedSender("app.b", 0);
		List<string> trace = [];
		using NativeBannerSuppressor second = new(residue, MarkerPath);
		second.Trace += (_, e) => trace.Add(e.Message);

		second.Apply();

		Assert.Contains(trace, m => m.Contains("отремонтирован", StringComparison.Ordinal));
		// После ремонта формула применена заново, но свежий снимок хранит ИСХОДНЫЙ
		// prior (1), а не остаток краха (0) — возврат возвращает пользователю его 1.
		second.Restore();
		Assert.Equal(1, residue.GetSenderDword("app.b", NativeBannerSuppressor.ShowBannerName));
	}

	[Fact]
	public void Apply_повторно_тихо_без_новых_записей()
	{
		FakeNotificationSettings registry = new();
		registry.SeedSender("app.a", 1);
		using NativeBannerSuppressor suppressor = new(registry, MarkerPath);
		suppressor.Apply();
		int writes = registry.SenderWriteCount;

		suppressor.Apply();

		Assert.Equal(writes, registry.SenderWriteCount);
	}

	[Fact]
	public void BlanketSender_гасит_нового_и_входит_в_возврат()
	{
		FakeNotificationSettings registry = new();
		using NativeBannerSuppressor suppressor = new(registry, MarkerPath);
		suppressor.Apply();

		suppressor.BlanketSender("app.new");

		Assert.Equal(0, registry.GetSenderDword("app.new", NativeBannerSuppressor.ShowBannerName));
		suppressor.Restore();
		Assert.Null(registry.GetSenderDword("app.new", NativeBannerSuppressor.ShowBannerName));
	}

	[Fact]
	public void BlanketSender_мимо_пустого_повтора_и_до_применения()
	{
		FakeNotificationSettings registry = new();
		using NativeBannerSuppressor suppressor = new(registry, MarkerPath);

		suppressor.BlanketSender(null);
		suppressor.BlanketSender("");
		Assert.Equal(0, registry.SenderWriteCount); // формула не применена — мимо

		suppressor.Apply(); // отправителей нет — записей blanket ноль
		Assert.Equal(0, registry.SenderWriteCount);

		suppressor.BlanketSender("app.new");
		suppressor.BlanketSender("app.new"); // повтор — одна запись
		Assert.Equal(1, registry.SenderWriteCount);
	}

	[Fact]
	public void Apply_сбой_посреди_записей_откатывает_и_стирает_маркер()
	{
		FakeNotificationSettings registry = new();
		registry.SeedSender("app.a", 1);
		registry.SeedSender("app.b", 1);
		registry.ThrowOnNthSenderWrite = 1; // глобальный записан, первый отправитель — сбой
		using NativeBannerSuppressor suppressor = new(registry, MarkerPath);

		Assert.Throws<IOException>(() => suppressor.Apply());

		// Откат: глобальный вернулся к «не было → единица», отправители при своих.
		Assert.Equal(1, registry.GetRootDword(NativeBannerSuppressor.GlobalToastsEnabledName));
		Assert.Equal(1, registry.GetSenderDword("app.a", NativeBannerSuppressor.ShowBannerName));
		Assert.Equal(1, registry.GetSenderDword("app.b", NativeBannerSuppressor.ShowBannerName));
		Assert.False(File.Exists(MarkerPath));
		// Состояние не активно: Restore тих, повторный Apply возможен.
		suppressor.Restore();
		registry.ThrowOnNthSenderWrite = null;
		suppressor.Apply();
		Assert.Equal(0, registry.GetSenderDword("app.a", NativeBannerSuppressor.ShowBannerName));
	}

	public void Dispose() => _dir.Dispose();
}
