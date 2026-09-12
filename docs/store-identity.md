# Store identity rnotify

Зарезервировано 12.09 (Partner Center, аккаунт физлица).

| Поле | Значение |
|---|---|
| Package/Identity/Name | `revealyan.RNotify` |
| Publisher | `CN=6F4182DB-0063-4273-87C8-59E15AFFCBCC` |
| PublisherDisplayName | `revealyan` |
| Отображаемое имя продукта | RNotify |

Для MSIX-манифеста (`Package.appxmanifest`):

```xml
<Identity Name="revealyan.RNotify"
          Publisher="CN=6F4182DB-0063-4273-87C8-59E15AFFCBCC"
          Version="0.1.0.0" />
<PublisherDisplayName>revealyan</PublisherDisplayName>
```

Локальная разработка без стора: self-signed сертификат с **Subject = тем же CN**,
чтобы дев-сборка ставилась с тем же identity, что и сторная (Package/Identity
одинаковы → один продукт, без конфликтов при переходе на Store-подпись).
Создание и доверие дев-сертификата — в скрипте `dev-cert.ps1` (появится в Э5).
