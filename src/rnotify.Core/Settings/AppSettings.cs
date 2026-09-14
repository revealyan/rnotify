namespace rnotify.Core.Settings;

/// <summary>
/// Настройки приложения (settings.json). Поле suppressWithoutListener —
/// гасить ли нативные баннеры формулой Э1, даже если согласие листенера не
/// получено. Дефолт false — безопасно: без consent карточек нет, и погашенные
/// баннеры оставили бы пользователя совсем без уведомлений.
/// </summary>
/// <param name="SuppressWithoutListener">Применять формулу Э1 при отказе/таймауте consent листенера.</param>
public sealed record AppSettings(bool SuppressWithoutListener = false);
