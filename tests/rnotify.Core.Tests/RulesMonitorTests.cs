using rnotify.Core.Rules;
using Xunit;

namespace rnotify.Core.Tests;

/// <summary>Дебаунс монитора: пачка сигналов в окне — одна перезагрузка (без реального FS).</summary>
public sealed class RulesMonitorTests
{
	[Fact]
	public async Task Пачка_сигналов_схлопывается_в_одну_перезагрузку()
	{
		using TempRulesDir dir = new();
		RulesStore store = new(dir.RulesPath);
		store.Save(new RulesConfig());
		RulesReloader reloader = new(store);

		// Окно дебаунса управляется извне: детерминизм без слипов (шов delay).
		TaskCompletionSource window = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
		int reloads = 0;
		reloader.Reloaded += (_, _) =>
		{
			reloads++;
			reloaded.TrySetResult();
		};

		using RulesMonitor monitor = new(store, reloader, debounce: TimeSpan.FromSeconds(1), delay: _ => window.Task);
		monitor.SignalChange();
		monitor.SignalChange();
		monitor.SignalChange();

		Assert.Equal(0, reloads); // окно дебаунса держит

		window.SetResult();
		await reloaded.Task;

		Assert.Equal(1, reloads); // три сигнала — одна перезагрузка
	}
}
