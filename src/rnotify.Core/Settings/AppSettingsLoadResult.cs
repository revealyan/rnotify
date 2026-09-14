namespace rnotify.Core.Settings;

/// <summary>Итог загрузки settings.json.</summary>
/// <param name="Settings">Настройки к работе (дефолт при любом сбое).</param>
/// <param name="CreatedDefault">Файла не было — создан дефолтный.</param>
/// <param name="Error">Сбой чтения/записи (битый json, нет прав) — наружу для панели; null — чисто.</param>
public sealed record AppSettingsLoadResult(AppSettings Settings, bool CreatedDefault, Exception? Error);
