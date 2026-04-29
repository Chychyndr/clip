# Clip

<p align="center">
  <img src="Clip/Assets/Square150x150Logo.png" width="96" height="96" alt="Clip app icon">
</p>

<p align="center">
  <img alt="Windows 11" src="https://img.shields.io/badge/Windows-11-0078D4?style=flat-square&logo=windows">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512BD4?style=flat-square&logo=dotnet">
  <img alt="WinUI 3" src="https://img.shields.io/badge/WinUI-3-2B7FFF?style=flat-square">
  <img alt="yt-dlp" src="https://img.shields.io/badge/yt--dlp-bundled-23B894?style=flat-square">
</p>

Clip is currently a Windows app for downloading video and audio from supported links. It uses `yt-dlp`, `ffmpeg`, and `ffprobe` under the hood, then gives the common download, trim, and compression controls a native WinUI interface. The download, queue, tool resolution, metadata cache, and ffmpeg/yt-dlp command logic are being separated into cross-platform services so a future macOS desktop UI can reuse the same core.

## Table of Contents

- [Description](#description)
- [Screenshots](#screenshots)
- [Features](#features)
- [Supported Services](#supported-services)
- [Tray Menu](#tray-menu)
- [TXT Import](#txt-import)
- [Build](#build)
- [Portable Build](#portable-build)
- [Installer](#installer)
- [macOS Status and Packaging](#macos-status-and-packaging)
- [Code Signing](#code-signing)
- [Troubleshooting](#troubleshooting)
- [Local Data](#local-data)

## Description

Clip is designed for quick link-based downloads. Paste a URL, drag a URL into the window, or import a `.txt` file with links. The app analyzes a pasted URL automatically, shows the preview when metadata is available, and queues downloads with configurable download and analysis concurrency.

## Screenshots


![Main window](docs/screenshots/main-window.png)

![Clip settings](docs/screenshots/clip-settings.png)

![Download progress](docs/screenshots/download-progress.png)

## Features

| Feature | Description |
| --- | --- |
| Single link downloads | Paste a supported URL and Clip analyzes it automatically. |
| TXT import | Import a `.txt` file that contains one or more supported links. |
| Drag and drop | Drop a link or `.txt` file into the app window. |
| Clipboard monitoring | Clip can detect supported URLs copied to the clipboard. |
| Queue | Downloads, metadata analysis, and ffmpeg post-processing use separate limits. |
| Formats | `MP4`, `MOV`, `WebM`, and `MP3`. |
| Resolution | `4K`, `1440p`, `1080p`, `720p`, `480p`, `360p`, or `Original`. |
| Target size | Keep the original size or compress to a custom megabyte target. |
| Clip range | Download the full media or save a selected trim range. |
| Clip output mode | Save only the cropped clip by default, or keep both cropped and original files. |
| History | Completed downloads are stored in local history. |

## Supported Services

| Service | Notes |
| --- | --- |
| YouTube / YouTube Shorts | Public links work without cookies. If YouTube asks for sign-in, Clip tries browser cookies. |
| X / Twitter | Availability depends on X platform restrictions. |
| Instagram | Some links require an active browser login. |
| TikTok | Public links usually work through `yt-dlp`. |
| Reddit | Links are resolved through `api.reddit.com`. |

Unsupported services are ignored by clipboard monitoring, TXT import, and drag and drop.

## Tray Menu

When "Hide to tray on close" is enabled, the Windows close button hides the window instead of exiting. The tray menu can show or hide Clip, paste a link, start the current download, pause or resume the queue, and exit the app.

## TXT Import

A TXT file can contain links on separate lines or mixed with common separators:

```text
https://youtu.be/example1
https://youtu.be/example2 https://www.tiktok.com/@user/video/123
https://x.com/user/status/123; https://www.instagram.com/reel/example/
https://youtu.be/example3: https://reddit.com/r/videos/comments/example
```

Duplicate links are queued once.

## Build

Place the required binaries in the platform-specific folder when bundling them:

```text
Clip\Resources\bin\win-x64\yt-dlp.exe
Clip\Resources\bin\win-x64\ffmpeg.exe
Clip\Resources\bin\win-x64\ffprobe.exe
Clip\Resources\bin\osx-x64\yt-dlp
Clip\Resources\bin\osx-x64\ffmpeg
Clip\Resources\bin\osx-x64\ffprobe
Clip\Resources\bin\osx-arm64\yt-dlp
Clip\Resources\bin\osx-arm64\ffmpeg
Clip\Resources\bin\osx-arm64\ffprobe
```

Clip also checks the legacy `Clip\Resources\bin` folder and then `PATH` if a bundled tool is not present.

Build commands:

```powershell
dotnet restore
dotnet build .\Clip.sln -c Debug -p:Platform=x64
```

Run from source:

```powershell
dotnet run --project .\Clip\Clip.csproj
```

## Portable Build

Create a portable build:

```powershell
.\scripts\publish-unpackaged.ps1
```

Output:

```text
artifacts\Clip-win-x64\Clip.exe
```

Move the whole `Clip-win-x64` folder when distributing the portable build.

## Installer

The current installer script builds the Windows installer.

Create a single-file installer:

```powershell
.\scripts\build-installer.ps1
```

Output:

```text
artifacts\ClipSetup.exe
```

The installer places the app in:

```text
%LOCALAPPDATA%\Programs\Clip
```

Uninstall:

```powershell
.\artifacts\ClipSetup.exe /uninstall
```

Clip also appears in Windows Settings -> Apps -> Installed apps. For local cleanup from the repository, use:

```powershell
.\scripts\uninstall.ps1
```

By default, uninstall removes only the app, shortcuts, and uninstall entry. History, settings, and logs in `%LOCALAPPDATA%\Clip` are preserved. To remove user data too:

```powershell
.\scripts\uninstall.ps1 -RemoveUserData
```

## macOS Status and Packaging

macOS support is prepared at the service layer, but this repository does not yet contain a shipping macOS UI project or macOS installer script. The current `Clip` project is WinUI 3 and targets `net8.0-windows10.0.19041.0`, so it cannot run on macOS directly.

The intended macOS path is:

1. Add a cross-platform desktop UI project, for example `Clip.Desktop` based on Avalonia UI.
2. Reference the shared core/services from that UI project.
3. Publish separate builds for Intel and Apple Silicon.
4. Package each build as a `.app` bundle.
5. Sign, notarize, and distribute as a `.dmg` or `.pkg`.

Expected runtime identifiers:

```text
osx-x64      macOS Intel
osx-arm64    macOS Apple Silicon
```

Bundled tools should be placed under platform-specific folders:

```text
Clip.Desktop.app/
  Contents/
    MacOS/
      Clip.Desktop
    Resources/
      bin/
        osx-x64/
          yt-dlp
          ffmpeg
          ffprobe
          aria2c
        osx-arm64/
          yt-dlp
          ffmpeg
          ffprobe
          aria2c
```

Clip also supports falling back to `PATH` when bundled tools are missing. On macOS, bundled binaries must have execute permission:

```zsh
chmod +x yt-dlp ffmpeg ffprobe aria2c
```

For public macOS distribution, the app bundle should be signed and notarized:

```zsh
codesign --deep --force --options runtime --sign "Developer ID Application: Your Name" Clip.Desktop.app
xcrun notarytool submit Clip.dmg --keychain-profile "notary-profile" --wait
xcrun stapler staple Clip.dmg
```

Gatekeeper may block unsigned or non-notarized builds. Local development builds can be opened manually from Finder, but releases should use Developer ID signing and notarization.

macOS local data paths:

```text
Downloads:      ~/Downloads/Clip
History:        ~/Library/Application Support/Clip/history.json
Settings:       ~/Library/Application Support/Clip/settings.json
Metadata cache: ~/Library/Application Support/Clip/cache/metadata
Log:            ~/Library/Logs/Clip/clip.log
```

Current repository status:

| Area | Status |
| --- | --- |
| Core download services | Prepared for Windows/macOS paths and tool resolution. |
| yt-dlp/ffmpeg binaries | Supports `win-x64`, `osx-x64`, and `osx-arm64` folder layout. |
| macOS UI | Not implemented yet. |
| `.app` bundle | Not implemented yet. |
| DMG/PKG installer | Not implemented yet. |
| Signing/notarization scripts | Not implemented yet. |

## Code Signing

Public releases should be signed with a trusted code-signing certificate. A self-signed certificate is useful only for local testing.

Create a test certificate:

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

Build and sign `Clip.exe` inside the installer, then sign `ClipSetup.exe`:

```powershell
$password = Read-Host "PFX password" -AsSecureString
.\scripts\build-installer.ps1 `
  -CertificatePath .\certs\clip-dev.pfx `
  -CertificatePassword $password
```

Verify the signature:

```powershell
signtool verify /pa /v .\artifacts\ClipSetup.exe
```

`signtool.exe` is included with the Windows SDK and is available from Visual Studio Developer PowerShell. Keep certificates, `.pfx` files, and passwords out of git.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| `Missing required binary` | Check `yt-dlp.exe`, `ffmpeg.exe`, and `ffprobe.exe` in `Clip\Resources\bin`. |
| `Sign in to confirm you're not a bot` | Sign in to YouTube in Chrome, Edge, Firefox, or Brave, then retry. |
| `Could not copy Chrome cookie database` | Close Chrome and retry. Clip can also try Edge, Firefox, or Brave if available. |
| `Clip could not locate the output file` | Update `yt-dlp.exe`, check the output folder, and verify write permissions. |
| SmartScreen warning | Sign the release with a trusted code-signing certificate. |
| Drag and drop did not work | Drop URL text, a browser address bar link, or a `.txt` file into the Clip window. |

## Local Data

```text
Downloads:      %USERPROFILE%\Downloads\Clip
History:        %APPDATA%\Clip\history.json
Settings:       %APPDATA%\Clip\settings.json
Metadata cache: %APPDATA%\Clip\cache\metadata
Log:            %APPDATA%\Clip\logs\clip.log
```

On macOS the equivalent data paths are `~/Library/Application Support/Clip` for settings/history/cache and `~/Library/Logs/Clip/clip.log` for logs.
