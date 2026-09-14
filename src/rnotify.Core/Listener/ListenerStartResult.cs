namespace rnotify.Core.Listener;

/// <summary>Итог запуска фида: статус доступа и базлайн (событий по нему нет).</summary>
/// <param name="Status">Статус consent; события живут только при Allowed.</param>
/// <param name="BaselineCount">Сколько уведомлений было в хранилище на старте (молча пропущенный backlog).</param>
/// <param name="Baseline">Записи базлайна (S6.4: догоняющие карточки при крахе — решает потребитель по floor).</param>
public sealed record ListenerStartResult(
	NotificationAccessStatus Status,
	int BaselineCount,
	IReadOnlyList<NotificationRecord> Baseline);
