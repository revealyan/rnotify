namespace rnotify.Core.Listener;

/// <summary>Уведомление хранилища Центра, очищенное от WinRT-типов.</summary>
/// <param name="Id">Системный Id — ключ диффа (уникален в сессии; факт Г2 спайка S5.2).</param>
/// <param name="Aumid">AUMID отправителя; null, если AppInfo отсутствует (спайк видел такое).</param>
/// <param name="Title">Первый текстовый элемент тоста; пустая строка — контент не прочитан.</param>
/// <param name="Body">Остальные текстовые элементы, соединённые переводом строки.</param>
/// <param name="RaisedAt">Время появления уведомления.</param>
public sealed record NotificationRecord(
	uint Id,
	string? Aumid,
	string Title,
	string Body,
	DateTimeOffset RaisedAt);
