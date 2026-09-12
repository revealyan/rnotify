using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace rnotify;

/// <summary>
/// Движок трёх экспериментов S5.2. Лог — timestamp-строки в IdentityPanel и в
/// файл (путь печатается первой строкой; истина по Г1 — внешний reg query,
/// read-back внутри пакета видит merged view). По доктрине Э1 прежние значения
/// реестра восстанавливаются на Exit.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
	Justification = "Жизненный цикл движка совпадает с процессом (см. прецедент App)")]
[SuppressMessage("Design", "CA1031:Do not catch general exception types",
	Justification = "Спайк: факт падения — тоже результат; логируем и идём дальше")]
internal sealed class Spike52Engine
{
	// Тестовый отправитель — тот же AUMID, что в spikes/tools/send-test-toast.ps1.
	private const string _settingsRoot = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
	private const string _psAumid = @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe";
	private const string _probeSubkeyPath = @"Software\revealyan\rnotify\Spike52";

	private static readonly object _sentinel = new();

	// Русский лог без BOM ломает PS 5.1 (канон §10a) — BOM обязателен.
	private static readonly Encoding _logEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
	private static readonly TimeSpan _consentTimeout = TimeSpan.FromSeconds(60);

	private readonly Dispatcher _dispatcher;
	private readonly string _logFilePath;
	private readonly Lock _fileGate = new();
	private readonly Lock _sinkGate = new();
	private readonly Lock _idsGate = new();
	private readonly SemaphoreSlim _diffGate = new(initialCount: 1, maxCount: 1);
	private readonly List<RegistryRestoreItem> _restoreItems = [];
	private readonly Queue<string> _pendingLines = new();
	private HashSet<uint> _seenIds = [];
	private StackPanel? _panel;
	private UserNotificationListener? _listener;
	private int _changeCount;

	internal Spike52Engine()
	{
		_dispatcher = Application.Current.Dispatcher;
		_logFilePath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			$"spike52_{DateTime.Now:yyyyMMdd_HHmmss}.log");
	}

	internal void RunSafeFireAndForget()
	{
		// Откат ключей Э1 по доктрине «при выходе — вернуть прежние значения».
		Application.Current.Exit += (_, _) => RestoreRegistryOnExit();
		_ = RunAsync(); // осознанный fire-and-forget: исключения по периметру RunAsync
	}

	private async Task RunAsync()
	{
		try
		{
			Log($"лог-файл: {_logFilePath}");
			await WaitForMainWindowAsync().ConfigureAwait(false);
			RunRegistryProbe();
			await RunConsentProbeAsync().ConfigureAwait(false);
			await RunListenerProbeAsync().ConfigureAwait(false);
			Log("спайк запущен: слать тест-тосты (send-test-toast.ps1); закрытие окна откатит ключи");
		}
		catch (Exception ex)
		{
			Log($"FATAL: {ex}");
		}
	}

	// Окно создаётся ПОСЛЕ возврата из OnStartup (StartupUri) — ждём его и
	// подключаем панель; таймаут — деградация в «только файл». Доступ к
	// Application.MainWindow требует UI-поток (VerifyAccess) — только через Dispatcher.
	private async Task WaitForMainWindowAsync()
	{
		DateTime deadline = DateTime.Now.AddSeconds(15);
		while (DateTime.Now < deadline)
		{
			MainWindow? window = await _dispatcher
				.InvokeAsync(() => Application.Current?.MainWindow as MainWindow)
				.Task.ConfigureAwait(false);
			if (window is not null)
			{
				AttachPanel(window);
				return;
			}

			await Task.Delay(millisecondsDelay: 100).ConfigureAwait(false);
		}

		Log("окно не появилось за 15 с — лог только в файл, эксперименты продолжаются");
	}

	private void AttachPanel(MainWindow window)
	{
		void Attach()
		{
			lock (_sinkGate)
			{
				_panel = window.IdentityPanel;
				while (_pendingLines.Count > 0)
				{
					_panel.Children.Add(CreateLogLine(_pendingLines.Dequeue()));
				}
			}
		}

		if (_dispatcher.CheckAccess())
		{
			Attach();
		}
		else
		{
			_ = _dispatcher.BeginInvoke(Attach);
		}
	}

	// --- Лог: единая точка. Файл — истина по порядку строк; панель — удобство. ---

	private void Log(string message)
	{
		string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}";
		lock (_fileGate)
		{
			File.AppendAllText(_logFilePath, line + Environment.NewLine, _logEncoding);
		}

		AppendToPanelSafe(line);
	}

	private void AppendToPanelSafe(string line)
	{
		StackPanel? panel;
		lock (_sinkGate)
		{
			panel = _panel;
			if (panel is null)
			{
				_pendingLines.Enqueue(line);
				return;
			}
		}

		try
		{
			if (_dispatcher.CheckAccess())
			{
				panel.Children.Add(CreateLogLine(line));
			}
			else
			{
				_ = _dispatcher.BeginInvoke(() => panel.Children.Add(CreateLogLine(line)));
			}
		}
		catch (Exception ex)
		{
			// Dispatcher уже погашен при закрытии — файл важнее панели; пишем
			// напрямую, минуя Log (иначе рекурсия в этот же catch).
			lock (_fileGate)
			{
				File.AppendAllText(_logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | панель недоступна: {ex.Message}{Environment.NewLine}", _logEncoding);
			}
		}
	}

	private static TextBlock CreateLogLine(string line) =>
		new() { Text = line, TextWrapping = TextWrapping.Wrap };

	// --- Г1: registry-виртуализация. Решение владельца: глобальный NOC-ключ
	//     не трогаем — per-app ключ механикой записи HKCU тот же. ---

	private void RunRegistryProbe()
	{
		Log("=== Г1: registry-виртуализация (истина — reg query снаружи) ===");
		ProbeKey(_probeSubkeyPath, "Probe", 1, label: "контрольный");
		ProbeKey($@"{_settingsRoot}\{_psAumid}", "ShowBanner", 0, label: "per-app");
	}

	private void ProbeKey(string subkeyPath, string valueName, int value, string label)
	{
		try
		{
			bool existed = false;
			object? oldValue = null;
			RegistryValueKind oldKind = RegistryValueKind.None;
			using (RegistryKey? read = Registry.CurrentUser.OpenSubKey(subkeyPath))
			{
				if (read is not null)
				{
					oldValue = read.GetValue(valueName, _sentinel);
					existed = !ReferenceEquals(oldValue, _sentinel);
					if (existed)
					{
						oldKind = read.GetValueKind(valueName);
					}
				}
			}

			using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subkeyPath))
			{
				key.SetValue(valueName, value, RegistryValueKind.DWord);
				object? readBack = key.GetValue(valueName, _sentinel);
				Log(
					$"Г1 ({label}): {subkeyPath}\\{valueName}={value} (DWord); "
					+ $"прежнее: {(existed ? $"{oldValue} ({oldKind})" : "отсутствует")}; "
					+ $"чтение в процессе (merged view): {(ReferenceEquals(readBack, _sentinel) ? "НЕТ" : $"{readBack}")}");
			}

			_restoreItems.Add(new RegistryRestoreItem(subkeyPath, valueName, existed ? oldValue : null, oldKind, existed));
		}
		catch (Exception ex)
		{
			Log($"Г1 ({label}): падение: {ex.Message} (HR=0x{ex.HResult:X8})");
		}
	}

	private void RestoreRegistryOnExit()
	{
		try
		{
			Log("=== откат Э1: восстановление прежних значений ===");
			foreach (RegistryRestoreItem item in _restoreItems)
			{
				using RegistryKey? key = Registry.CurrentUser.OpenSubKey(item.SubkeyPath, writable: true);
				if (key is null)
				{
					Log($"откат: ключ исчез — пропуск {item.SubkeyPath}");
					continue;
				}

				if (item.Existed)
				{
					key.SetValue(item.ValueName, item.OldValue!, item.Kind);
					Log($"откат: {item.SubkeyPath}\\{item.ValueName} → {item.OldValue} ({item.Kind})");
				}
				else
				{
					key.DeleteValue(item.ValueName, throwOnMissingValue: false);
					Log($"откат: {item.SubkeyPath}\\{item.ValueName} удалён (его не было до спайка)");
				}
			}
		}
		catch (Exception ex)
		{
			Log($"откат: падение: {ex.Message} (HR=0x{ex.HResult:X8})");
		}
	}

	// --- Г3: consent листенера. Диалог вызывается после появления окна,
	//     чтобы не потерялся под ним; висячий ответ наблюдается отдельно. ---

	private async Task RunConsentProbeAsync()
	{
		Log("=== Г3: consent листенера (RequestAccessAsync) ===");
		try
		{
			_listener = UserNotificationListener.Current;
			Task<UserNotificationListenerAccessStatus> request = _listener.RequestAccessAsync().AsTask();
			Task finished = await Task.WhenAny(request, Task.Delay(_consentTimeout)).ConfigureAwait(false);
			if (finished != request)
			{
				Log($"Г3: нет ответа за {_consentTimeout.TotalSeconds:0:#} с (диалог ждёт клика?) — продолжаем");
				_ = request.ContinueWith(
					t => Log(t.Status == TaskStatus.RanToCompletion
						? $"Г3: поздний ответ: {t.Result}"
						: $"Г3: поздний ответ: {t.Status} {t.Exception?.GetBaseException().Message}"),
					TaskScheduler.Default);
				return;
			}

			Log(request.Status == TaskStatus.RanToCompletion
				? $"Г3: статус доступа: {request.Result}"
				: $"Г3: задача завершилась как {request.Status}: {request.Exception?.GetBaseException().Message}");
		}
		catch (Exception ex)
		{
			Log($"Г3: падение: {ex.Message} (HR=0x{ex.HResult:X8})");
		}
	}

	// --- Г2: NotificationChanged (в unpackaged PoC подписка/событие падали
	//     0x80070490) + диф снапшота по Id. Контент — Э2, здесь только Id/AUMID. ---

	private async Task RunListenerProbeAsync()
	{
		Log("=== Г2: NotificationChanged + диф снапшота по Id ===");
		try
		{
			if (_listener is null)
			{
				Log("Г2: листенер не создан (Г3 упал до него) — пропуск");
				return;
			}

			_listener.NotificationChanged += OnNotificationChanged;
			Log("Г2: подписка NotificationChanged оформлена");
			await DiffSnapshotAsync(_listener, initial: true).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log($"Г2: падение: {ex.Message} (HR=0x{ex.HResult:X8})");
		}
	}

	// Обычный void-хендлер (async void запрещён): минимум сразу, снапшот — задачей.
	private void OnNotificationChanged(UserNotificationListener sender, object args)
	{
		int n = Interlocked.Increment(ref _changeCount);
		Log($"Г2: NotificationChanged #{n}");
		_ = DiffSnapshotAsync(sender, initial: false);
	}

	private async Task DiffSnapshotAsync(UserNotificationListener listener, bool initial)
	{
		// События летят пачками — диффы строго по очереди.
		await _diffGate.WaitAsync().ConfigureAwait(false);
		try
		{
			IReadOnlyList<UserNotification> snapshot =
				await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask().ConfigureAwait(false);
			HashSet<uint> fresh = [.. snapshot.Select(u => u.Id)];
			lock (_idsGate)
			{
				foreach (UserNotification u in snapshot.Where(u => !_seenIds.Contains(u.Id)))
				{
					Log($"Г2: + id={u.Id} AUMID={u.AppInfo?.AppUserModelId ?? "<null>"}");
				}

				foreach (uint gone in _seenIds.Where(id => !fresh.Contains(id)))
				{
					Log($"Г2: − id={gone}");
				}

				_seenIds = fresh;
			}

			Log($"Г2: снапшот: {snapshot.Count} уведомлений{(initial ? " (первый)" : "")}");
		}
		catch (Exception ex)
		{
			Log($"Г2: снапшот упал: {ex.Message} (HR=0x{ex.HResult:X8})");
		}
		finally
		{
			_diffGate.Release();
		}
	}
}
