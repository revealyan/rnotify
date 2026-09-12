using System.Diagnostics.CodeAnalysis;
using System.Windows;
using rnotify.Core;

namespace rnotify;

/// <summary>
/// Точка композиции приложения. Хост тонкий: вся логика — в rnotify.Core.
/// Шов S5.2: спайк-код (запись ключей Э1, UserNotificationListener,
/// NotificationChanged) вставляется сюда, в OnStartup.
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
	}

	protected override void OnExit(ExitEventArgs e)
	{
		// Отдать мьютекс может только владелец.
		_singleInstance?.ReleaseMutex();
		_singleInstance?.Dispose();
		base.OnExit(e);
	}
}
