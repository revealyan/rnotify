# SPIKE S5.2 — packaged-режим: registry-виртуализация Э1, NotificationChanged, consent

Дата: 12.09.2026. Машина: Win 11 Pro 26200 (25H2-ветка), пользователь alexa.
Хост спайка: `src/rnotify/Spike52Engine.cs` (packaged WPF, PFN
`revealyan.RNotify_5tv6nhn7amdk4`), лог — IdentityPanel + файл в LocalCache.

## Гипотезы (до эксперимента)

- **Г1а**: запись ключа Э1 из packaged-процесса видна снаружи (`reg query` из
  непакетного процесса) → виртуализации нет, формула Э1 применима как есть.
- **Г1б** (ожидаемая): запись НЕ видна → ушла в приватный улей пакета →
  формула Э1 из packaged не действует на WpnService; лечится capability
  `unvirtualizedResources` (проверка — раунд 2).
- **Г2а**: `NotificationChanged` в packaged с identity работает (в unpackaged
  PoC подписка/событие падали `0x80070490` → там был поллинг 1 с).
- **Г3а**: consent-диалог `RequestAccessAsync` в packaged появляется (в
  unpackaged возвращал `Allowed` молча).

Ограничение по решению владельца: глобальный `NOC_GLOBAL_SETTING_TOASTS_ENABLED`
на живой машине не трогаем — per-app ключ механикой записи HKCU тот же.

## Метод

1. Установка подписанного msix (`Add-AppxPackage`), запуск через
   `shell:AppsFolder\<PFN>!App`.
2. Спайк при старте: Г1 — две пробы (`Probe=1` контрольная в
   `Software\revealyan\rnotify\Spike52`, `ShowBanner=0` per-app для AUMID
   `powershell.exe`) с read-back; Г3 — `RequestAccessAsync`; Г2 — подписка
   `NotificationChanged` + первый снапшот.
3. Снаружи: `reg query` до/после; тест-тост `spikes/tools/send-test-toast.ps1`.
4. Закрытие окна (WM_CLOSE) → откат Э1 → контрольный `reg query`.

## Раунд 1 — без capability (12.09, 23:04–23:06)

| # | Факт | Наблюдение |
|---|---|---|
| 1 | **Виртуализация есть (Г1б подтверждена, Г1а опровергнута)** | `reg query` снаружи: `ShowBanner` у тестового AUMID отсутствует, контрольной ветки нет — при том что read-back внутри процесса видит оба значения (merged view). Физически записи ушли в `%LOCALAPPDATA%\Packages\<PFN>\SystemAppData\Helium\User.dat` (обновлён в момент записи) |
| 2 | **Формула Э1 из packaged не действует** | ключ в реальном улье не появился → WpnService (сторонний процесс, без merged view пакета) его не видит; тест-тост показался нативным баннером |
| 3 | **NotificationChanged работает (Г2а подтверждена)** | подписка без исключений; тест-тост 23:05:43 → событие #1 через ~0.7 с, диф по Id увидел `id=8411` от AUMID powershell. `0x80070490` не воспроизвёлся |
| 4 | **Снапшот читается из packaged** | `GetNotificationsAsync(Toast)` — 23 уведомления при старте (id + AUMID верны, включая `revealyan.rhub`, ASUS-ассистент) |
| 5 | **Consent: Allowed без диалога (Г3а опровергнута)** | `RequestAccessAsync` → `Allowed` за 82 мс, диалога не было — как в unpackaged |
| 6 | Откат Э1 на Exit работает | закрытие окна → оба значения удалены (виртуально), реальный улей чист до/после |
| 7 | Грабля WPF | `Application.MainWindow` кидает `InvalidOperationException` (VerifyAccess) с pool-потока после `ConfigureAwait(false)` — доступ только через `Dispatcher.InvokeAsync` (починено в раунде 1) |

Вывод раунда 1: **MSIX registry virtualization перехватывает ключи Э1 —
suppress-механика продукта требует `unvirtualizedResources`** (раунд 2);
листенер в packaged лучше unpackaged-эталона: живые события есть, consent тихий.

## Раунд 2 — с capability `unvirtualizedResources` (12.09, 23:09–23:38)

Два под-раунда — важная развилка:

### 2a. Только capability — НЕДОСТАТОЧНО

`<rescap:Capability Name="unvirtualizedResources" />` в манифесте
установленного пакета (проверено `Get-AppxPackageManifest`), но записи спайка
по-прежнему не видны снаружи (`reg query`), read-back внутри видит (merged
view). Грабли по пути: MakeAppx отвергает `rescap3:`/`rescap4:`-варианты
элемента — правильное объявление обычное `rescap:Capability` с именем
`unvirtualizedResources`.

### 2b. Пара «capability + Flexible Virtualization» — работает

Добавлено в `Properties` манифеста:

```xml
<desktop6:RegistryWriteVirtualization>disabled</desktop6:RegistryWriteVirtualization>
```

| # | Факт | Наблюдение |
|---|---|---|
| 8 | **Записи доходят до реального улья** | снаружи видны оба: контрольный `Software\revealyan\rnotify\Spike52\Probe=1` и per-app `ShowBanner=0x0` |
| 9 | **Эффект формулы Э1 из packaged подтверждён** (пара опыт/контроль, BMP-диф зоны тостов, DPI-aware): ключ `ShowBanner=0`, записанный процессом пакета → тост в хранилище (`NotificationChanged #4`, `id=8422`), баннера нет (диф D500=0, D1500=99 ≈ шум 0); ключ удалён → баннер показан (D1500=2108). Примечание: на этой машине баннер появляется позже 500 мс (D500=0 в обоих прогонах) — точка замера не раньше ~1.5 с |
| 10 | **Откат на Exit действует в реальном улье** | закрытие окна → оба значения удалены настоящим `DeleteValue`, машина чиста (per-app ключ = состояние до спайка) |
| 11 | Consent/листенер раунда 1 воспроизводятся | `Allowed` без диалога; события и диф стабильны (27 уведомлений к концу раунда) |

**Итог спайка**: формула Э1 применима из packaged-приложения только при паре
`unvirtualizedResources` (capability) **+** `desktop6:RegistryWriteVirtualization=disabled`
(Properties). Оба — restricted/особые declarations: для стора потребуется
обоснование в сабмишне (S5.3). Листенер в packaged лучше unpackaged-эталона:
живые события `NotificationChanged` вместо поллинга, consent тихий `Allowed`.
