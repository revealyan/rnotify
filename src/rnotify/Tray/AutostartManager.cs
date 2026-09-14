using Windows.ApplicationModel;

namespace rnotify.Tray;

/// <summary>
/// Автозапуск через MSIX StartupTask (TaskId rnotifyStartup из манифеста,
/// uap5:Extension). Чекбокс трея зовёт <see cref="SetAsync"/>; состояние
/// системы — StartupTaskState (Enabled/Disabled/DisabledByUser/DisabledByPolicy).
/// unpackaged-запуск (F5): GetForCurrentPackageAsync кидает — вызывающий
/// ловит и пишет строкой в панель.
/// </summary>
internal static class AutostartManager
{
	private const string _taskId = "rnotifyStartup";

	/// <summary>Включить/выключить автозапуск; итог — состояние системы.</summary>
	internal static async Task<StartupTaskState> SetAsync(bool enabled)
	{
		StartupTask task = await FindTaskAsync().ConfigureAwait(false);
		if (enabled)
		{
			// RequestEnableAsync: система решает (политика/юзер мог выключить
			// в Task Manager) — потому возвращаемое состояние, а не desired.
			return await task.RequestEnableAsync().AsTask().ConfigureAwait(false);
		}

		task.Disable();
		return task.State;
	}

	private static async Task<StartupTask> FindTaskAsync()
	{
		IReadOnlyList<StartupTask> tasks = await StartupTask.GetForCurrentPackageAsync().AsTask().ConfigureAwait(false);
		return tasks.FirstOrDefault(t => string.Equals(t.TaskId, _taskId, StringComparison.Ordinal))
			?? throw new InvalidOperationException($"StartupTask «{_taskId}» не найден в пакете (манифест?)");
	}
}
