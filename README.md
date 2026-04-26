# Clip

<p align="center">
  <strong>Нативное Windows-приложение для скачивания видео, аудио и коротких клипов.</strong><br>
  WinUI 3 оболочка вокруг <code>yt-dlp</code>, <code>ffmpeg</code> и <code>ffprobe</code>.
</p>

<p align="center">
  <img alt="Windows" src="https://img.shields.io/badge/Windows-10%2022H2%2B%20%7C%2011-0078D4?style=flat-square&logo=windows">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8-512BD4?style=flat-square&logo=dotnet">
  <img alt="WinUI" src="https://img.shields.io/badge/WinUI-3-5C2D91?style=flat-square">
</p>

## О проекте

Clip помогает сохранять медиа по ссылке в аккуратном Windows-приложении. Он показывает превью, позволяет выбрать формат и качество, поддерживает обрезку по времени, ведет очередь загрузок и хранит локальную историю.

Интерфейс сделан в стиле Windows 11: Mica-фон, полупрозрачные карточки, компактная компоновка, понятные состояния и быстрые действия из трея.

## Возможности

- Вставка ссылки, drag and drop и отслеживание буфера обмена.
- Превью с обложкой, названием, платформой, автором и длительностью.
- Форматы: `MP4`, `MOV`, `WebM`, `MP3`.
- Выбор качества: `4K`, `1440p`, `1080p`, `720p`, `480p`, `360p`.
- Обрезка фрагмента по началу и концу.
- Очередь загрузок до 3 активных задач.
- Отмена, повтор, открытие файла и открытие папки.
- История загрузок в локальном JSON-файле.
- Иконка в системном трее с быстрыми командами.
- Встроенные `yt-dlp`, `ffmpeg` и `ffprobe`.
- Разбор Reddit-ссылок через `api.reddit.com`.
- Автопоиск cookies для Instagram в установленных браузерах.

## Стек

- C# 12
- .NET 8
- WinUI 3
- Windows App SDK
- Unpackaged desktop build

## Что нужно для сборки

Для сборки на Windows 11 установите:

- Visual Studio 2022
- Workload `.NET desktop development`
- инструменты Windows App SDK / WinUI
- .NET 8 SDK

Также положите исполняемые файлы в папку:

```text
Clip/
  Resources/
    bin/
      yt-dlp.exe
      ffmpeg.exe
      ffprobe.exe
```

Где взять бинарники:

- `yt-dlp.exe`: https://github.com/yt-dlp/yt-dlp/releases
- `ffmpeg.exe` и `ffprobe.exe`: https://www.gyan.dev/ffmpeg/builds/

## Запуск из исходников

Откройте PowerShell в корне репозитория:

```powershell
dotnet restore
dotnet build .\Clip.sln -c Debug -p:Platform=x64
dotnet run --project .\Clip\Clip.csproj
```

Через Visual Studio:

1. Откройте `Clip.sln`.
2. Выберите платформу `x64`.
3. Назначьте `Clip` стартовым проектом.
4. Нажмите `F5`.

## Как собрать EXE

В репозитории есть готовый скрипт публикации:

```powershell
.\scripts\publish-unpackaged.ps1
```

После выполнения появится папка:

```text
artifacts/
  Clip-win-x64/
    Clip.exe
    Clip.dll
    Resources/
      bin/
        yt-dlp.exe
        ffmpeg.exe
        ffprobe.exe
```

Готовый файл для запуска:

```text
artifacts\Clip-win-x64\Clip.exe
```

Запускайте `Clip.exe` на Windows 11 из папки `Clip-win-x64`. Вся папка должна оставаться целиком, потому что приложение использует файлы из `Resources\bin`.

## Где хранятся данные

```text
Загрузки:  %USERPROFILE%\Downloads\Clip
Настройки: %LOCALAPPDATA%\Clip\settings.json
История:   %LOCALAPPDATA%\Clip\history.json
```

## Публичный релиз

Для публикации рекомендуется подписать приложение сертификатом. Без подписи Windows Defender SmartScreen может показать предупреждение при первом запуске.
