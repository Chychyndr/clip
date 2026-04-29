# Clip

Clip is a desktop app for downloading video and audio by URL through `yt-dlp`, then processing media through `ffmpeg` and `ffprobe`.

The repository is being migrated from a Windows-only WinUI app to a cross-platform .NET architecture with an Avalonia desktop app. The existing WinUI project is still kept for Windows compatibility during the transition, while the new cross-platform entry point is `src/Clip.App`.

## Supported Platforms

| Platform | Runtime | Status |
| --- | --- | --- |
| Windows x64 | `win-x64` | Supported by the existing WinUI app and the new Avalonia app. |
| macOS Intel | `osx-x64` | Supported by the new Avalonia app publish path. Manual app bundle/signing verification is still required. |
| macOS Apple Silicon | `osx-arm64` | Supported by the new Avalonia app publish path. Manual app bundle/signing verification is still required. |

## Project Structure

```text
src/
  Clip.Core/
    Models/
    Services/
    ViewModels/
    DownloadQueue/
    History/
    Settings/
    Tools/
  Clip.App/
    App.axaml
    MainWindow.axaml
    Platform/
    Themes/
  Clip.Platform/
    Windows/
    MacOS/
resources/
  bin/
    win-x64/
    macos-arm64/
    macos-x64/
scripts/
  publish-windows.ps1
  publish-macos.sh
```

`Clip.Core` contains platform-neutral logic: models, settings, metadata cache, history, queue limits, process execution, yt-dlp command building, ffmpeg command building, progress parsing, tool resolution, and shared view models.

`Clip.Platform` contains platform implementations for app paths, browser cookie source detection, tray/clipboard placeholders, and future OS-specific services.

`Clip.App` is the Avalonia UI. It references `Clip.Core` and `Clip.Platform` and avoids WinUI-only APIs.

The legacy `Clip/` WinUI project remains in the solution so current Windows installer and portable scripts keep working while migration continues.

## Features

- Single URL downloads.
- Automatic metadata analysis after URL paste or clipboard URL detection.
- Video/audio modes.
- Format choices: `MP4`, `MOV`, `WebM`, `MP3`, `Original`, `Best`.
- Resolution selection.
- TXT import with empty-line filtering, duplicate removal, and basic URL validation.
- Download queue with separate concurrency limits for metadata analysis, downloads, and ffmpeg work.
- Download history.
- Metadata JSON cache.
- Stable yt-dlp progress parsing through `--progress-template`.
- Safe external process execution through `ProcessStartInfo.ArgumentList`.
- Fast trim through ffmpeg stream copy and exact trim through re-encode.
- Compression presets and hardware encoder detection.
- Optional experimental aria2c integration.

## External Tools

Bundled tools should be placed in:

```text
resources/bin/win-x64/yt-dlp.exe
resources/bin/win-x64/ffmpeg.exe
resources/bin/win-x64/ffprobe.exe

resources/bin/macos-arm64/yt-dlp
resources/bin/macos-arm64/ffmpeg
resources/bin/macos-arm64/ffprobe

resources/bin/macos-x64/yt-dlp
resources/bin/macos-x64/ffmpeg
resources/bin/macos-x64/ffprobe
```

Optional aria2c binaries can be placed in the same platform folders.

Tool lookup order:

1. `Resources/bin/<platform>/` next to the app.
2. Legacy runtime folders such as `Resources/bin/osx-arm64/`.
3. `Resources/bin/`.
4. The app directory.
5. `PATH`.

On macOS, Clip attempts to set executable permission for bundled tools. If Gatekeeper blocks a downloaded binary, remove quarantine after verifying the file source:

```zsh
xattr -dr com.apple.quarantine yt-dlp ffmpeg ffprobe
chmod +x yt-dlp ffmpeg ffprobe
```

## Local Data

Windows:

```text
Settings:       %LOCALAPPDATA%\Clip\settings.json
History:        %LOCALAPPDATA%\Clip\history.json
Metadata cache: %LOCALAPPDATA%\Clip\cache\metadata
Logs:           %LOCALAPPDATA%\Clip\logs\clip.log
Downloads:      %USERPROFILE%\Downloads\Clip
```

macOS:

```text
Settings:       ~/Library/Application Support/Clip/settings.json
History:        ~/Library/Application Support/Clip/history.json
Metadata cache: ~/Library/Application Support/Clip/cache/metadata
Logs:           ~/Library/Logs/Clip/clip.log
Downloads:      ~/Downloads/Clip
```

## Build

Restore dependencies:

```bash
dotnet restore
```

Build the solution:

```bash
dotnet build
```

Build only the cross-platform Avalonia app:

```bash
dotnet build src/Clip.App/Clip.App.csproj
```

Run the Avalonia app:

```bash
dotnet run --project src/Clip.App/Clip.App.csproj
```

Run tests:

```bash
dotnet run --project Clip.Tests/Clip.Tests.csproj
```

## Publish

Windows Avalonia build:

```powershell
.\scripts\publish-windows.ps1
```

Manual Windows publish:

```bash
dotnet publish src/Clip.App/Clip.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/Clip-win-x64
```

macOS builds:

```bash
./scripts/publish-macos.sh
```

Manual macOS publish:

```bash
dotnet publish src/Clip.App/Clip.App.csproj -c Release -r osx-arm64 --self-contained true -o artifacts/Clip-macos-arm64
dotnet publish src/Clip.App/Clip.App.csproj -c Release -r osx-x64 --self-contained true -o artifacts/Clip-macos-x64
```

## Windows Installer

The existing installer script still targets the legacy WinUI project:

```powershell
.\scripts\build-installer.ps1
```

Output:

```text
artifacts\ClipSetup.exe
```

If publishing fails because `yt-dlp.exe` is locked, close running Clip instances and retry. The publish scripts also fall back to a timestamped output folder when the previous artifact directory cannot be removed.

## macOS Packaging

The current macOS publish path produces a self-contained app output folder. A production release should package that output as a `.app` bundle, then sign and notarize it.

Typical release steps:

1. Publish `osx-arm64` and `osx-x64`.
2. Copy the published files into `Clip.app/Contents/MacOS/`.
3. Place bundled tools under `Clip.app/Contents/MacOS/Resources/bin/macos-arm64/` or `Clip.app/Contents/MacOS/Resources/bin/macos-x64/`.
4. Ensure tool permissions with `chmod +x`.
5. Add `Info.plist`.
6. Sign the app bundle.
7. Create a `.dmg`.
8. Submit the `.dmg` for notarization.
9. Staple the notarization ticket.

Example commands:

```zsh
codesign --deep --force --options runtime --sign "Developer ID Application: Your Name" Clip.app
hdiutil create -volname "Clip" -srcfolder Clip.app -ov -format UDZO Clip.dmg
xcrun notarytool submit Clip.dmg --keychain-profile "notary-profile" --wait
xcrun stapler staple Clip.dmg
```

Unsigned development builds can be run locally, but public macOS releases should be signed and notarized to avoid Gatekeeper warnings.

## GitHub Releases

Tagged releases trigger `.github/workflows/build.yml` and produce:

```text
Clip-win-x64.zip
Clip-macos-arm64.zip
Clip-macos-x64.zip
```

The workflow builds `src/Clip.App/Clip.App.csproj` for each runtime and uploads zip artifacts.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| `yt-dlp was not found` | Place `yt-dlp` in the matching `resources/bin/<platform>` folder or install it in `PATH`. |
| `ffmpeg was not found` | Place `ffmpeg` and `ffprobe` in the matching platform folder or install them in `PATH`. |
| macOS permission error | Run `chmod +x yt-dlp ffmpeg ffprobe` and remove quarantine only after verifying the source. |
| Gatekeeper blocks the app | Sign and notarize release builds. For local dev builds, open manually from Finder after confirming the build source. |
| NuGet restore reads a user config unexpectedly | Use the repository `NuGet.Config` with `dotnet restore --configfile NuGet.Config`. |
| `Access denied` while cleaning artifacts | Close running Clip instances or use a new output folder. |

## Development Notes

The migration is intentionally staged. The new Avalonia app already compiles and uses the cross-platform core, but some Windows-only features such as native tray behavior and continuous clipboard monitoring are represented through interfaces and safe placeholders until platform-native implementations are completed.
