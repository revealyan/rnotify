namespace rnotify.Core.Settings;

/// <summary>
/// Настройки приложения (settings.json). Поле suppressWithoutListener —
/// гасить ли нативные баннеры формулой Э1, даже если согласие листенера не
/// получено. Дефолт false — безопасно: без consent карточек нет, и погашенные
/// баннеры оставили бы пользователя совсем без уведомлений. Поле autostart —
/// зеркалит состояние чекбокса трея (S6.1): перезапуск с системой через
/// MSIX StartupTask; приложение держит файл и WinRT-состояние синхронно.
/// Поле language (S6.2) — override языка UI: "ru" | "en" | null (= язык ОС;
/// нейтральный en, ru — сателлит). Читается на старте: смена — перезапуском.
/// </summary>
/// <param name="SuppressWithoutListener">Применять формулу Э1 при отказе/таймауте consent листенера.</param>
/// <param name="Autostart">Запускаться с системой (StartupTask; чекбокс трея).</param>
/// <param name="Language">Язык UI: "ru" | "en" | null — язык ОС.</param>
/// <param name="CatchUpLimit">S6.4: сколько догоняющих карточек показать при старте (0 — не догонять); остальное молча в floor.</param>
/// <param name="CatchUpSticky">S6.4: догоняющие без TTL — читает юзер, закрывает юзер; подача по свободным слотам стека.</param>
/// <param name="CardScreen">S7.1: экран зоны карточек: "cursor" (где мышь, дефолт) | "primary" | "active" (переднее окно).</param>
/// <param name="HistoryHotkey">S7.2: хоткей панели истории ("Win+Shift+N" — дефолт; парсинг терпит Ctrl/Alt/Shift/Win + клавиша).</param>
/// <param name="HistoryLimit">S7.2: глубина истории в записях (0 — не хранить; дефолт 1000).</param>
/// <param name="HistoryOnlyShown">S7.2: панель истории по умолчанию показывает только показанные карточками (фильтр снимается в самой панели).</param>
public sealed record AppSettings(
	bool SuppressWithoutListener = false,
	bool Autostart = false,
	string? Language = null,
	int CatchUpLimit = 25,
	bool CatchUpSticky = true,
	string? CardScreen = null,
	string? HistoryHotkey = null,
	int HistoryLimit = 1000,
	bool HistoryOnlyShown = true);
