namespace rnotify.Core.History;

/// <summary>
/// Запись истории уведомлений (S7.2). Храним ВСЁ с пометкой действия —
/// фильтрация «только показанные» решается на просмотре (настройка
/// historyOnlyShown), сырая история полна и пригодна для разбора правил.
/// </summary>
/// <param name="RaisedUnix">Unix-время (секунды) прихода уведомления.</param>
/// <param name="Aumid">AUMID отправителя (может быть null — AppInfo отсутствовал).</param>
/// <param name="SenderName">Резолвнутое имя на момент записи (может протухнуть — не перерезолвим).</param>
/// <param name="Title">Заголовок (первый текст-элемент контракта 22621).</param>
/// <param name="Body">Тело (остальные элементы, join).</param>
/// <param name="Action">Вердикт: "show" | "mute" | "delete".</param>
public sealed record HistoryEntry(
	long RaisedUnix,
	string? Aumid,
	string? SenderName,
	string Title,
	string Body,
	string Action);
