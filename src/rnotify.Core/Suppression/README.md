# Suppression — подавление нативного показа (Э1, сторя S1.2)

Формула Э1 (канон §10): глобально `NOC_GLOBAL_SETTING_TOASTS_ENABLED=0` +
blanket per-app `ShowBanner=0` всем отправителям; доставка в хранилище
Центра жива (корм листенера Э2). Состав:

- `INotificationSettingsRegistry` — шов над кустом HKCU `...\Notifications\Settings`
  (прод — `RegistryNotificationSettings`, тесты — фейк).
- `NativeBannerSuppressor` — применение/возврат/blanket новых отправителей,
  маркер-снапшот `suppression.json` (крах-безопасность: следующий старт чинит).
- `SuppressionSnapshot` — прежние значения (null = не было), он же — содержимое маркера.

Ветку consent (гасить ли без согласия листенера) решает настройка
`suppressWithoutListener` (`../Settings/settings.json`).
