# ROADMAP rnotify

Формат: эпики из запроса владельца (12.09). Канон фактов — `C:\devs\docs\win11-notifications-control.md`.

## Э1 — нативный показ = 0 мс ✅ (12.09, спайк)

Формула валидирована глазами владельца (дважды, reminder-сценарий):
глобальный тумблер + blanket per-app `ShowBanner=0`; доставка в хранилище жива.
Продукт-часть (применение/восстановление конфига) — в стори S1.2.

## Э2 — свои уведомления для всего системного ⏳ (S2.1 ✅)

- S2.1 Листенер в ядре: consent → события → диффы; чтение контракта 22621
  (`Notification.Visual.Bindings → GetTextElements`); backlog-фильтр.
  ✅ 13.09 — фид `NotificationFeed` в `rnotify.Core` (базлайн молча, диффы по
  Id за семафором, события Added/Removed/SnapshotFailed), Spike52-код удалён;
  10 юнит-тестов + arch живы. Живой прогон: контент читается и в packaged
  (title/body, русский+эмодзи), `AppInfo` даёт только AUMID (DisplayName нет —
  имя/иконка отдельной сторей), время — `CreationTime`; факты: канон §10c.
  Разбор: [S2.1-listener-core.md](S2.1-listener-core.md).
- Остаток Э2: анти-дубли между запусками (floor на диске) — отложен до
  рендера (решение владельца: backlog молча, решать нечего пока нет карточек);
  имя/иконка отправителя (резолв вне листенера).

## Э3 — политика настроек ⏳

Группы фильтров; per-rule: match (regex/exact/contains) × app/title/body,
action show/mute/delete, ttl, killNative?, click (close/focus), цвет группы.
Формат конфига + редактор.

## Э4 — кастомный UI ⏳

Спайк-сравнение WPF vs WinUI 3 (карточка, топмост-оверлей, мультимонитор,
DPI 150%), затем реализация: стек карточек, темы, док-панель истории.

## Э5 — MSIX + Store ⏳ (текущий)

- S5.1 Скелет: MSIX-first каркас, self-signed dev identity (стор-identity
  подставим позже одним полем Publisher). ✅ 12.09 — WPF-хост + Core + arch-тесты,
  подписанный msix собирается билдом; CI зелёный; пакет установлен и проверен
  (identity совпадает, single-instance жив). Разбор: [S5.1-skeleton.md](S5.1-skeleton.md).
- S5.2 Спайк: пишет ли packaged-приложение ключи Э1 мимо registry
  виртуализации (запасной путь — unvirtualizedResources в манифесте);
  работает ли `NotificationChanged` с identity; consent листенера в packaged.
  ✅ 12.09 — виртуализация есть (Helium\User.dat), лечит пара
  `unvirtualizedResources` **+** `RegistryWriteVirtualization=disabled`
  (capability одной — мало); эффект Э1 подтверждён парой опыт/контроль;
  `NotificationChanged` работает (0x80070490 не воспроизвёлся), consent —
  `Allowed` без диалога. Разбор: [S5.2-packaged-spike.md](S5.2-packaged-spike.md);
  факты: канон §10c, `spikes/SPIKE-S5.2.md`.
- S5.3a Релизный пайплайн: тег `v*` → Actions → MSIX → GitHub Release
  (unsigned). ✅ 13.09 — версия манифеста из тега в checkout-копии; грабля
  `$10`-группы в regex-replacement поймана локальной рельсой до пуша
  (элемент Identity удалялся целиком). Живой тег `v0.1.0` — по команде
  владельца. Разбор: [S5.3a-release-pipeline.md](S5.3a-release-pipeline.md).
- S5.3b Стор-сабмит (после Э2): Partner Center, обоснование restricted
  declarations (`unvirtualizedResources` + `RegistryWriteVirtualization`,
  канон §10c), релизная подпись.
- Статус аккаунта: зарегистрирован как РФ — на сабмите может быть отказ;
  план Б: GitHub + Certum (~€60-90/год) + winget-community.
