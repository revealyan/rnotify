# ROADMAP rnotify

Формат: эпики из запроса владельца (12.09). Канон фактов — `C:\devs\docs\win11-notifications-control.md`.

## Э1 — нативный показ = 0 мс ✅ (12.09, спайк)

Формула валидирована глазами владельца (дважды, reminder-сценарий):
глобальный тумблер + blanket per-app `ShowBanner=0`; доставка в хранилище жива.
Продукт-часть (применение/восстановление конфига) — в стори S1.2.

## Э2 — свои уведомления для всего системного ⏳

Механизм доказан (листенер читает хранилище при подавлении). Стори:
чтение контракта 22621 (`Notification.Visual`), поллинг/события,
backlog-фильтр, анти-дубли (floor на диске).

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
  подписанный msix собирается билдом (разбор: [S5.1-skeleton.md](S5.1-skeleton.md));
  статусы: код готов → review → установка глазами владельца → done.
- S5.2 Спайк: пишет ли packaged-приложение ключи Э1 мимо registry
  виртуализации (запасной путь — unvirtualizedResources в манифесте);
  работает ли `NotificationChanged` с identity; consent листенера в packaged.
- S5.3 CI: GitHub Actions → MSIX → Release; стор-сабмишн.
- Статус аккаунта: зарегистрирован как РФ — на сабмите может быть отказ;
  план Б: GitHub + Certum (~€60-90/год) + winget-community.
