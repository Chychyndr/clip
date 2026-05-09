# Third-Party Notices

This file tracks third-party command-line tools that Clip may bundle in release builds. It is a release checklist and notice file, not legal advice. Before publishing a public build, update the exact version, source URL, binary provider, license files, and build configuration for every bundled executable.

## Bundled Tool Checklist

For each release, record:

- Exact executable file name and version.
- Download URL or build source URL.
- SHA-256 checksum used for the bundled file.
- License name and local copy of the license text.
- For FFmpeg and ffprobe, the full build configuration from `ffmpeg -buildconf`.

## yt-dlp

- Executable: `yt-dlp.exe`
- Upstream: <https://github.com/yt-dlp/yt-dlp>
- Release checksums: yt-dlp publishes `SHA2-256SUMS` and signature files in GitHub Releases.
- License note: the yt-dlp source repository is under the Unlicense, but upstream documents that PyInstaller-bundled executables include GPLv3+ code and that the combined executable is GPLv3+.
- Required release action: include the upstream license files and `THIRD_PARTY_LICENSES.txt` that correspond to the bundled yt-dlp executable.

## FFmpeg and ffprobe

- Executables: `ffmpeg.exe`, `ffprobe.exe`
- Upstream: <https://ffmpeg.org/>
- Legal notes: <https://www.ffmpeg.org/legal.html>
- License note: FFmpeg is LGPLv2.1+ by default. Builds configured with GPL components become GPLv2+ for the FFmpeg binary. Additional codec libraries can add further license obligations.
- Required release action: record the binary provider, exact version, full `ffmpeg -buildconf` output, and whether the build is LGPL or GPL.

## aria2

- Executable: `aria2c.exe`
- Upstream: <https://aria2.github.io/>
- Source: <https://github.com/aria2/aria2>
- License note: aria2 is GPL-2.0.
- Required release action: include the GPL license text and record the exact binary provider and checksum.

## Current Repository State

The repository keeps placeholder folders under `resources/bin/*` and does not commit third-party binaries. Public release artifacts that include binaries must fill in this notice file for the exact files being distributed.
