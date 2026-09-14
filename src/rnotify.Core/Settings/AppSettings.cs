namespace rnotify.Core.Settings;

/// <summary>
/// Настройки приложения (settings.json). Поле suppressWithoutListener —
/// гасить ли нативные баннеры формулой Э1, даже если согласие листенера не
/// получено. Дефолт false — безопасно: без consent карточек нет, и погашенные
/// баннеры оставили бы пользователя совсем без уведомлений. Поле autostart —
/// зеркалит состояние чекбокса трея (S6.1): перезапуск с системой через
/// MSIX StartupTask; приложение держит файл и WinRT-состояние синхронно.
/// </summary>
/// <param name="SuppressWithoutListener">Применять формулу Э1 при отказе/таймауте consent листенера.</param>
/// <param name="Autostart">Запускаться с системой (StartupTask; чекбокс трея).</param>
public sealed record AppSettings(bool SuppressWithoutListener = false, bool Autostart = false);
