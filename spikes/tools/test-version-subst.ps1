# Тест подстановки версии из тега в манифест (рельса release.yml, S5.3a).
# Факт: replacement "`$1$version.0`$2" при $version='0.9.3' разворачивается
# PowerShell'ом в строку "$10.9.3.0$2"; .NET-regex парсит "$10" как группу 10
# (несуществующая) — подстановка калечит атрибут Version. Лечение — фигурные
# скобки: "`${1}$version.0`${2}".
# Рельса PS 5.1 (§10a): манифест в UTF-8 без BOM — явный -Encoding UTF8.
param(
    [string] $Version = '0.9.3'
)

# Кириллица в stdout (PS 5.1 из bash конвертит в '?' на пайпе):
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$source = 'src/rnotify/Package.appxmanifest'
$copy = Join-Path $env:TEMP 'appxmanifest.test'
Copy-Item $source $copy -Force

function Get-IdentityVersion([string] $text) {
    [regex]::Match($text, '<Identity[^>]*Version="[^"]*"').Value
}

# Демонстрация сломанной формы (не использовать):
$broken = (Get-Content $copy -Raw -Encoding UTF8) -replace '(<Identity[^>]*Version=")[\d.]+(")', "`$1$Version.0`$2"
"сломанная (`$1 без скобок): $(Get-IdentityVersion $broken)"

# Рабочая форма — как в release.yml:
$fixed = (Get-Content $copy -Raw -Encoding UTF8) -replace '(<Identity[^>]*Version=")[\d.]+(")', "`${1}$Version.0`${2}"
$identity = Get-IdentityVersion $fixed
"рабочая   (`${1}):   $identity"

if ($identity -notmatch [regex]::Escape("Version=""$Version.0""")) {
    "FAIL: ожидаемая версия $Version.0 не подставилась"
    exit 1
}
Remove-Item $copy
"OK"
