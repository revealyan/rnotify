namespace rnotify.Core.Listener;

/// <summary>Итог запуска фида: статус доступа и размер базлайна.</summary>
/// <param name="Status">Статус consent; события живут только при Allowed.</param>
/// <param name="BaselineCount">Сколько уведомлений было в хранилище на старте (молча пропущенный backlog).</param>
public sealed record ListenerStartResult(NotificationAccessStatus Status, int BaselineCount);
