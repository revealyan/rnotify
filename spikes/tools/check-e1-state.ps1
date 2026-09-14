# Проверка состояния формулы Э1 (живой прогон S1.2): глобальный тумблер,
# маркер-снапшот, settings.json, blanket ShowBanner=0 у отправителей.
$root = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings'
$global = (Get-Item $root -ErrorAction SilentlyContinue).GetValue('NOC_GLOBAL_SETTING_TOASTS_ENABLED')
"global NOC_GLOBAL = $(if ($null -ne $global) { $global } else { '<нет значения>' })"
$marker = Join-Path $env:USERPROFILE '.rnotify\suppression.json'
"marker: $(Test-Path $marker) ($(if (Test-Path $marker) { (Get-Item $marker).Length } else { '-' }) bytes)"
$settings = Join-Path $env:USERPROFILE '.rnotify\settings.json'
"settings: $(Test-Path $settings)"
if (Test-Path $settings) { Get-Content $settings }
$keys = Get-ChildItem $root
$blanketed = @($keys | Where-Object { $_.GetValue('ShowBanner') -eq 0 })
"senders: всего $($keys.Count), ShowBanner=0 у $($blanketed.Count)"
