using Windows.UI.Notifications;

namespace rnotify.Core.Listener;

/// <summary>
/// Чтение контента тоста по контракту 22621: сырой XML из проекции исчез, текст —
/// только через Visual.Bindings → GetTextElements() (канон §9, опыт старого rnotif
/// — unpackaged-гипотеза). Пустые строки = «контент не прочитан» — продукт
/// деградирует корректно; факт packaged-режима фиксируется живым прогоном S2.1.
/// </summary>
internal static class ToastContentReader
{
	/// <summary>Первый текстовый элемент — заголовок, остальные — тело (join переводом строки).</summary>
	internal static (string Title, string Body) Read(Notification? notification)
	{
		if (notification?.Visual?.Bindings is not { Count: > 0 } bindings)
		{
			return (string.Empty, string.Empty);
		}

		foreach (NotificationBinding binding in bindings)
		{
			IReadOnlyList<AdaptiveNotificationText> texts = binding.GetTextElements();
			if (texts.Count == 0)
			{
				continue;
			}

			string title = texts[0].Text ?? string.Empty;
			string body = string.Join(Environment.NewLine, texts.Skip(1).Select(t => t.Text ?? string.Empty));
			return (title, body);
		}

		return (string.Empty, string.Empty);
	}
}
