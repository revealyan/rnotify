namespace rnotify;

/// <summary>
/// Фасад спайка S5.2: packaged-режим (виртуализация ключей Э1, consent и
/// NotificationChanged листенера). Static-поле держит движок живым — подписка
/// должна пережить OnStartup. Double-run исключён mutex'ом в <see cref="App"/>.
/// </summary>
internal static class Spike52
{
	private static Spike52Engine? _active;

	/// <summary>Вызов из App.OnStartup. Синхронна: весь async — fire-and-forget внутри.</summary>
	internal static void Run()
	{
		if (_active is not null)
		{
			return; // теоретическая защита от повторного вызова
		}

		var engine = new Spike52Engine();
		_active = engine;
		engine.RunSafeFireAndForget();
	}
}
