# rnotify

Свой диспетчер уведомлений Windows 11: система не показывает ничего сама —
приложение гасит нативный рендер на корню, читает поток уведомлений и
показывает всё своими карточками по правилам (по приложению и по контенту).

**Статус**: старт 12.09.2026. Закрыты: формула Э1 (спайк), MSIX-каркас S5.1,
packaged-спайк S5.2 (ключи Э1 из пакета — пара capability +
`RegistryWriteVirtualization`; `NotificationChanged` живой), релизный пайплайн
S5.3a (тег → Release, unsigned), листенер в ядре S2.1 (события, контент
22621 — packaged подтверждён живым прогоном, backlog-фильтр). Далее: Э3 —
политика правил; floor-анти-дубли — вместе с рендером Э4.

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

## Релизы

Тег `v*` на main → [release-workflow](.github/workflows/release.yml) собирает
MSIX (версия манифеста — из тега: `v0.1.2` → `0.1.2.0`) и вкладывает его в
GitHub Release. Пакет без подписи: для установки на свою машину подписать
дев-сертом (после однократного `tools/dev-cert.ps1`):

```powershell
signtool sign /fd SHA256 /f certs\rnotify-dev.pfx /p <пароль из tools/dev-cert.ps1> rnotify_0.1.0.0_x64.msix
Add-AppxPackage .\rnotify_0.1.0.0_x64.msix
```

Релизная подпись (стор-серт или Certum) — S5.3b после Э2.
