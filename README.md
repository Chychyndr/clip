# Clip

<p align="center">
  <img alt="Windows 11" src="https://img.shields.io/badge/Windows-11-0078D4?style=flat-square&logo=windows">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512BD4?style=flat-square&logo=dotnet">
  <img alt="WinUI 3" src="https://img.shields.io/badge/WinUI-3-2B7FFF?style=flat-square">
  <img alt="yt-dlp" src="https://img.shields.io/badge/yt--dlp-bundled-23B894?style=flat-square">
</p>

Clip - Windows-приложение для скачивания видео и аудио по ссылке. Внутри используются `yt-dlp`, `ffmpeg` и `ffprobe`.

## Что умеет

| Возможность | Как работает |
| --- | --- |
| Одна ссылка | Вставьте ссылку, нажмите `Analyze`, затем `Download`. |
| TXT со ссылками | Нажмите `TXT` и выберите файл со ссылками. |
| Drag and drop | Перетащите ссылку или `.txt` файл в окно приложения. |
| Буфер обмена | Автоматически подхватываются только ссылки на поддерживаемые видеосервисы. |
| Очередь | Загрузки идут последовательно, по одной. |
| Форматы | `MP4`, `MOV`, `WebM`, `MP3`. |
| Качество | `4K`, `1440p`, `1080p`, `720p`, `480p`, `360p`, `Original`. |
| Обрезка | Можно указать начало и конец фрагмента. |
| История | Готовые загрузки сохраняются в локальной истории. |

## Сервисы

Clip распознает эти платформы:

| Сервис | Нюансы |
| --- | --- |
| YouTube / YouTube Shorts | Публичные ссылки идут без cookies. Если YouTube просит вход, Clip пробует cookies из браузера. |
| X / Twitter | Доступность зависит от ограничений самого X. |
| Instagram | Для части ссылок нужен вход в браузере. |
| TikTok | Публичные ссылки обычно работают через `yt-dlp`. |
| Reddit | Ссылки разбираются через `api.reddit.com`. |

Ссылки на другие сайты игнорируются при автопроверке буфера обмена, импорте TXT и drag and drop.

## Трей

Если включено скрытие в трей, кнопка закрытия Windows прячет окно. Через иконку в трее можно развернуть Clip, скрыть окно, поставить очередь на паузу, открыть настройки или полностью выключить приложение.

## TXT импорт

Файл может содержать ссылки в любом из таких вариантов:

```text
https://youtu.be/example1
https://youtu.be/example2 https://www.tiktok.com/@user/video/123
https://x.com/user/status/123; https://www.instagram.com/reel/example/
https://youtu.be/example3: https://reddit.com/r/videos/comments/example
```

Повторяющиеся ссылки добавляются один раз.

## Сборка

Положите бинарники сюда:

```text
Clip\Resources\bin\yt-dlp.exe
Clip\Resources\bin\ffmpeg.exe
Clip\Resources\bin\ffprobe.exe
```

Команды:

```powershell
dotnet restore
dotnet build .\Clip.sln -c Debug -p:Platform=x64
```

Запуск из исходников:

```powershell
dotnet run --project .\Clip\Clip.csproj
```

## Готовый EXE

Portable-сборка:

```powershell
.\scripts\publish-unpackaged.ps1
```

Результат:

```text
artifacts\Clip-win-x64\Clip.exe
```

Папку `Clip-win-x64` нужно переносить целиком.

Установщик одним файлом:

```powershell
.\scripts\build-installer.ps1
```

Результат:

```text
artifacts\ClipSetup.exe
```

Установщик кладет приложение в:

```text
%LOCALAPPDATA%\Programs\Clip
```

## Подпись сертификатом

Для публичного релиза нужен code-signing certificate от доверенного центра сертификации. Самоподписанный сертификат подходит для локальной проверки.

Создать тестовый сертификат:

```powershell
mkdir certs
$cert = New-SelfSignedCertificate `
  -Type CodeSigningCert `
  -Subject "CN=Clip Dev" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -HashAlgorithm SHA256

$password = Read-Host "PFX password" -AsSecureString
Export-PfxCertificate `
  -Cert $cert `
  -FilePath .\certs\clip-dev.pfx `
  -Password $password
```

Собрать и подписать `Clip.exe` внутри установщика и сам `ClipSetup.exe`:

```powershell
$password = Read-Host "PFX password" -AsSecureString
.\scripts\build-installer.ps1 `
  -CertificatePath .\certs\clip-dev.pfx `
  -CertificatePassword $password
```

Проверить подпись:

```powershell
signtool verify /pa /v .\artifacts\ClipSetup.exe
```

`signtool.exe` входит в Windows SDK и доступен из Visual Studio Developer PowerShell. Сертификаты, `.pfx` файлы и пароли храните вне git.

## Частые ошибки

| Ошибка | Решение |
| --- | --- |
| `Missing required binary` | Проверьте `yt-dlp.exe`, `ffmpeg.exe`, `ffprobe.exe` в `Clip\Resources\bin`. |
| `Sign in to confirm you're not a bot` | Войдите в YouTube в Chrome, Edge, Firefox или Brave и повторите загрузку. Clip подключает cookies только после такой ошибки. |
| `Could not copy Chrome cookie database` | Закройте Chrome и повторите попытку. Если есть Edge, Firefox или Brave, Clip попробует их тоже. |
| `Clip could not locate the output file` | Обновите `yt-dlp.exe`, проверьте папку загрузки и права записи. |
| SmartScreen предупреждает при запуске | Подпишите релиз доверенным code-signing certificate. |
| Drag and drop не сработал | Перетаскивайте сам текст ссылки, ссылку из адресной строки или `.txt` файл в окно Clip. |

## Данные

```text
Загрузки:  %USERPROFILE%\Downloads\Clip
История:   %LOCALAPPDATA%\Clip\history.json
Настройки: %LOCALAPPDATA%\Clip\settings.json
Лог:       %LOCALAPPDATA%\Clip\crash.log
```
