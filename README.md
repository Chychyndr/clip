# Clip

<p align="center">
  <img src="Clip/Assets/Square150x150Logo.png" width="96" height="96" alt="Clip app icon">
</p>

<p align="center">
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%2F11-0078D4?style=flat-square&logo=windows">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512BD4?style=flat-square&logo=dotnet">
  <img alt="WinUI 3" src="https://img.shields.io/badge/WinUI-3-2B7FFF?style=flat-square">
  <img alt="yt-dlp" src="https://img.shields.io/badge/yt--dlp-bundled-23B894?style=flat-square">
</p>

Clip is a Windows-only WinUI 3 desktop app for downloading video and audio from supported links. It uses `yt-dlp`, `ffmpeg`, and `ffprobe` for media work, and keeps a native Windows shell with tray behavior, clipboard monitoring, queue controls, history, trim, compression, and update helpers.

## Contents

- [Features](#features)
- [Supported Services](#supported-services)
- [Tray Menu](#tray-menu)
- [TXT Import](#txt-import)
- [Tool Security](#tool-security)
- [Build](#build)
- [Portable Build](#portable-build)
- [Installer](#installer)
- [Code Signing](#code-signing)
- [License](#license)
- [Third-Party Binaries](#third-party-binaries)
- [Troubleshooting](#troubleshooting)
- [Local Data](#local-data)

## Features

| Feature | Description |
| --- | --- |
| Native Windows UI | WinUI 3 window, Mica backdrop, Windows tray icon, and Windows file pickers. |
| Single link downloads | Paste or drop a supported URL and Clip analyzes it automatically. |
| TXT import | Import a `.txt` file with one or more links. Duplicate links are queued once. |
| Clipboard monitoring | Clip can detect supported URLs copied to the clipboard. |
| Queue | Multiple downloads can be queued, paused, resumed, retried, canceled, and opened from history. |
| Metadata cache | Video metadata is cached locally to make repeated analysis faster. |
| Formats | `MP4`, `MOV`, `WebM`, and `MP3`. |
| Resolution | `4K`, `1440p`, `1080p`, `720p`, `480p`, `360p`, or `Original`. |
| Target size | Keep the original size or compress to a custom megabyte target. |
| Clip range | Download the full media or save a selected trim range. |
| Processing options | Fast or exact trim, compression mode, hardware encoder preference, and concurrent `yt-dlp` fragments. |
| Tool updates | Check and update bundled `yt-dlp` from the app with SHA-256 verification. |

## Supported Services

| Service | Notes |
| --- | --- |
| YouTube / YouTube Shorts | Public links work without cookies. If YouTube asks for sign-in, Clip can try browser cookies. |
| X / Twitter | Availability depends on X platform restrictions. |
| Instagram | Some links require an active browser login. |
| TikTok | Public links usually work through `yt-dlp`. |
| Reddit | Links are resolved through `api.reddit.com`. |

Unsupported services are ignored by clipboard monitoring, TXT import, and drag and drop. Manual downloads still depend on `yt-dlp` support, but automatic clipboard analysis is limited to the services above.

## Tray Menu

When `Hide to tray on close` is enabled, the Windows close button hides the window instead of exiting. The tray menu can show or hide Clip, paste a link, start the current download, pause or resume the queue, and exit the app.

## TXT Import

A TXT file can contain links on separate lines or mixed with common separators:

```text
https://youtu.be/example1
https://youtu.be/example2 https://www.tiktok.com/@user/video/123
https://x.com/user/status/123; https://www.instagram.com/reel/example/
https://youtu.be/example3: https://reddit.com/r/videos/comments/example
```

Duplicate links are queued once. Imported links are added to the normal queue immediately, so they are analyzed and downloaded with the same progress, cancellation, retry, history, and privacy behavior as pasted links. Low-level `yt-dlp -a links.txt` command support exists in the core command builder for a future dedicated batch job UI.

## Tool Security

Release builds use bundled tools only by default:

```text
Resources\bin\win-x64\yt-dlp.exe
Resources\bin\win-x64\ffmpeg.exe
Resources\bin\win-x64\ffprobe.exe
Resources\bin\win-x64\aria2c.exe
```

`PATH` fallback is disabled unless the user explicitly enables `Allow external tools from PATH` in Settings and restarts the app. When external tools are enabled, the Check buttons show the full executable path and mark it as an external `PATH` executable.

The built-in `yt-dlp` updater only downloads assets from `https://github.com/yt-dlp/yt-dlp/releases/download/`, downloads the release checksum file, verifies SHA-256, then starts the downloaded executable with `--version` before replacing the bundled binary. Failed updates remove the `.download` file and keep a `.bak` backup for rollback.

## Build

Requirements:

- Windows 10 19041 or newer.
- .NET 8 SDK.
- Windows App SDK dependencies restored through NuGet.

Place optional bundled tools here:

```text
resources\bin\win-x64\yt-dlp.exe
resources\bin\win-x64\ffmpeg.exe
resources\bin\win-x64\ffprobe.exe
```

Build commands:

```powershell
dotnet restore .\Clip.sln --configfile NuGet.Config -p:Platform=x64
dotnet build .\Clip.sln -c Debug -p:Platform=x64
```

Run from source:

```powershell
dotnet run --project .\Clip\Clip.csproj -p:Platform=x64
```

Run tests:

```powershell
dotnet run --project .\Clip.Tests\Clip.Tests.csproj -c Release
```

Security checks:

```powershell
dotnet list .\Clip.sln package --vulnerable --include-transitive
dotnet list .\Clip.sln package --deprecated
```

## Portable Build

Create a portable Windows build:

```powershell
.\scripts\publish-unpackaged.ps1
```

Output:

```text
artifacts\Clip-win-x64\Clip.exe
```

Move the whole `Clip-win-x64` folder when distributing the portable build.

## Installer

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

## License

Clip is distributed under GPL-3.0-only. New source files should include this SPDX header:

```csharp
// SPDX-License-Identifier: GPL-3.0-only
```

## Third-Party Binaries

The repository keeps placeholder folders under `resources/bin/*` and does not commit third-party binaries. Before publishing a release that bundles `yt-dlp`, `ffmpeg`, `ffprobe`, or `aria2c`, update [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) with exact versions, binary providers, checksums, licenses, and FFmpeg build configuration.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| `Missing required binary` | Check `yt-dlp.exe`, `ffmpeg.exe`, and `ffprobe.exe` in `resources\bin\win-x64`. Public builds do not use `PATH` unless the setting is enabled. |
| `Sign in to confirm you're not a bot` | Sign in to YouTube in Chrome, Edge, Firefox, or Brave, then retry. |
| `Could not copy Chrome cookie database` | Close Chrome and retry. Clip can also try Edge, Firefox, or Brave if available. |
| `Clip could not locate the output file` | Update `yt-dlp.exe`, check the output folder, and verify write permissions. |
| SmartScreen warning | Sign the release with a trusted code-signing certificate. |
| Drag and drop did not work | Drop URL text, a browser address bar link, or a `.txt` file into the Clip window. |

## Local Data

```text
Downloads: %USERPROFILE%\Downloads\Clip
History:   %LOCALAPPDATA%\Clip\history.json
Settings:  %LOCALAPPDATA%\Clip\settings.json
Log:       %LOCALAPPDATA%\Clip\logs\clip.log
Cache:     %LOCALAPPDATA%\Clip\cache\metadata
```

History can be disabled in Settings. `Store only history titles` keeps titles but omits saved URLs, formats, resolutions, and local output paths. Metadata cache can also be disabled or cleared from Settings; oversized and expired cache files are pruned automatically before new metadata is saved.
