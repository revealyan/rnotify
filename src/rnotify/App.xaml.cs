using System.Diagnostics.CodeAnalysis;
using System.Windows;
using rnotify.Core;

namespace rnotify;

/// <summary>
/// Точка композиции приложения. Хост тонкий: вся логика — в rnotify.Core.
/// Композиция Э2 — MainWindow.OnLoaded создаёт NotificationFeed (листенер
/// живёт в окне); спайк S5.2 удалён (факты — канон §10c, spikes/SPIKE-S5.2.md).
/// S6.1: жизнь процесса = трей (ShutdownMode=OnExplicitShutdown): окно
/// создаётся руками (StartupUri убран — автостарт стартует скрытым), второй
/// инстанс сигналит живому «покажи панель» и уходит.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
	Justification = "Жизненный цикл App совпадает с процессом; мьютекс освобождается в OnExit, сигнал — фоновый поток")]
public partial class App : Application
{
	private Mutex? _singleInstance;
	private EventWaitHandle? _showPanelSignal;
	private Thread? _showPanelLoop;

	protected override void OnStartup(StartupEventArgs e)
	{
		// Инвариант продукта: диспетчер уведомлений всегда один. Второй инстанс
		// разворачивает панель живого и молча уходит (в спайке S5.2 дубль =
		// двойной шум в HKCU и листенере).
		_singleInstance = new Mutex(initiallyOwned: true, ProductIdentity.MutexName, out bool isFirst);
		if (!isFirst)
		{
			if (EventWaitHandle.TryOpenExisting(ProductIdentity.ShowPanelEventName, out EventWaitHandle? signal))
			{
				signal.Set();
				signal.Dispose();
			}

			// Мьютекс чужой: закрыть хэндл без ReleaseMutex и уйти.
			_singleInstance.Dispose();
			_singleInstance = null;
			Shutdown();
			return;
		}

		_showPanelSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ProductIdentity.ShowPanelEventName);
		base.OnStartup(e);

		// StartupUri убран: окно создаём руками — автозапуск стартует скрытым
		// (детект активации StartupTask — коммит 2, пока старт всегда видимый).
		MainWindow window = new();
		MainWindow = window;
		window.Show();

		// Ждём сигнал «покажи панель» фоновым потоком: второй запуск — самый
		// естественный путь к панели свёрнутого демона. IsBackground: выход
		// процесса убивает ожидание сам.
		_showPanelLoop = new Thread(WaitShowPanel) { Name = "rnotify-show-panel", IsBackground = true };
		_showPanelLoop.Start();
	}

	private void WaitShowPanel()
	{
		while (true)
		{
			_showPanelSignal?.WaitOne();
			_ = Dispatcher.BeginInvoke(() =>
			{
				MainWindow?.Show();
				MainWindow?.Activate();
			});
		}
	}

	protected override void OnExit(ExitEventArgs e)
	{
		// Отдать мьютекс может только владелец.
		_singleInstance?.ReleaseMutex();
		_singleInstance?.Dispose();
		base.OnExit(e);
	}
}
