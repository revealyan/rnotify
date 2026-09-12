# rnotify

Свой диспетчер уведомлений Windows 11: система не показывает ничего сама —
приложение гасит нативный рендер на корню, читает поток уведомлений и
показывает всё своими карточками по правилам (по приложению и по контенту).

**Статус**: старт 12.09.2026. Закрыты: формула Э1 (спайк), MSIX-каркас S5.1,
packaged-спайк S5.2 (ключи Э1 из пакета — пара capability +
`RegistryWriteVirtualization`; `NotificationChanged` живой). Далее: Э2 —
листенер в ядре.

- Канон ресёрча и формула Э1: `C:\devs\docs\win11-notifications-control.md` (§9–10)
- Роадмап: [docs/stories/ROADMAP.md](docs/stories/ROADMAP.md)
- Дистрибуция: GitHub Releases → MS Store → winget(msstore); план Б — GitHub+Certum

## Сборка и установка (dev)

Однократно (требует консоль с правами администратора — включает dev-mode и
импортирует дев-сертификат в доверенные):

```powershell
powershell -ExecutionPolicy Bypass -File tools/dev-cert.ps1
```

Каждая сборка (сертификат на месте → пакет подписывается автоматически):

```powershell
dotnet build -c Release src/rnotify
```

(каждую команду копируйте отдельной строкой — стрелки в примерах выше только
для связности повествования)

Установка пакета и проверка identity глазами (окно показывает Name/Publisher/
Version из манифеста):

```powershell
Add-AppxPackage src\rnotify\AppPackages\rnotify_0.1.0.0_x64_Test\rnotify_0.1.0.0_x64.msix
Get-AppxPackage revealyan.RNotify        # Name/Version/Publisher
# запуск: окно RNotify; второй запуск молча выходит (single-instance)
Get-AppxPackage revealyan.RNotify | Remove-AppxPackage   # удаление
```

Без дев-сертификата (чистый клон, CI) пакет собирается без подписи — сборка
остаётся зелёной.
