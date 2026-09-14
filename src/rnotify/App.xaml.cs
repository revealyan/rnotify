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

		// StartupUri убран: окно создаём руками. Активация StartupTask (запуск
		// системы) — старт сразу в трей (решение план-гейта S6.1): Show+Hide в
		// одном кадре — Loaded стреляет (инициализация живёт в OnLoaded), а
		// рендера между вызовами не было, окна не видно.
		bool startupActivation = IsStartupActivation();
		MainWindow window = new();
		MainWindow = window;
		if (startupActivation)
		{
			window.Show();
			window.Hide();
		}
		else
		{
			window.Show();
		}

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

	// Активация StartupTask (запуск системы) — AppLifecycle от WinSDK-проекций.
	// WASDK 2.4: GetActivatedEventArgs — instance-член текущего AppInstance.
	[SuppressMessage("Design", "CA1031:Do not catch general exception types",
		Justification = "unpackaged/нестандартная активация: не определять StartupTask — показать окно как при ручном запуске")]
	private static bool IsStartupActivation()
	{
		try
		{
			return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
				== Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
		}
		catch (Exception)
		{
			return false;
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
