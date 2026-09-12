using System.Diagnostics.CodeAnalysis;
using System.Windows;
using rnotify.Core;

namespace rnotify;

/// <summary>
/// Точка композиции приложения. Хост тонкий: вся логика — в rnotify.Core.
/// Шов S5.2 занят: спайк packaged-режима — <see cref="Spike52"/> в OnStartup.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
	Justification = "Жизненный цикл App совпадает с процессом; мьютекс освобождается в OnExit")]
public partial class App : Application
{
	private Mutex? _singleInstance;

	protected override void OnStartup(StartupEventArgs e)
	{
		// Инвариант продукта: диспетчер уведомлений всегда один. Второй инстанс
		// молча выходит (в спайке S5.2 дубль = двойной шум в HKCU и листенере).
		// Э4: при смене UI-стека заменить на AppInstance-redirect.
		_singleInstance = new Mutex(initiallyOwned: true, ProductIdentity.MutexName, out bool isFirst);
		if (!isFirst)
		{
			// Мьютекс чужой: закрыть хэндл без ReleaseMutex и уйти.
			_singleInstance.Dispose();
			_singleInstance = null;
			Shutdown();
			return;
		}

		base.OnStartup(e);

		// Шов S5.2: спайк packaged-режима (виртуализация ключей Э1, consent,
		// NotificationChanged). Окно появится после возврата из OnStartup —
		// Spike52 сам дождётся его и ведёт лог в IdentityPanel + файл.
		Spike52.Run();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		// Отдать мьютекс может только владелец.
		_singleInstance?.ReleaseMutex();
		_singleInstance?.Dispose();
		base.OnExit(e);
	}
}
