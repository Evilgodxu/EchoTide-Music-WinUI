<div align="center">

<img src="docs/icon.png" width="96" alt="Echo Tide" />

# Echo Tide · Windows

**Immersive listening, faithful to the original sound**

**English** | [简体中文](README.zh-CN.md)

![License](https://img.shields.io/badge/license-AGPL--3.0-blue)
![Platform](https://img.shields.io/badge/platform-Windows-brightgreen)
![Version](https://img.shields.io/badge/version-0.0.1-informational)
![.NET](https://img.shields.io/badge/.NET-10-purple)
![WinUI](https://img.shields.io/badge/WinUI-3-blue)
![Windows App SDK](https://img.shields.io/badge/Windows%20App%20SDK-2.5.1-blue)
![Arch](https://img.shields.io/badge/arch-x64-orange)

</div>

## Overview

Echo Tide for Windows is a native desktop rebuild of the [Android app](https://github.com/Evilgodxu/EchoTide-Music)'s landscape player, written in C# on WinUI 3. It follows the same composition — cover on the left, lyrics on the right, title bar and transport controls surfacing when the pointer reaches the window edges — and plays through the system media pipeline rather than an embedded browser.

This build covers the local library only: scanning, playback, lyrics, cover art, the queue and settings all work offline, and the app itself has no network functionality. Android-only capabilities such as online search, proxy sources and metadata editing have not been ported yet; they will land in stages.

## Features

### Library

- Folder-based scanning: add any number of music folders, or start from the Windows Music library; only the selected folders are scanned.
- Supports MP3, FLAC, WAV, M4A/M4B/MP4, AAC, WMA, OGG and Opus; anything shorter than 30 seconds is treated as a sound effect and skipped.
- Metadata is read from the files themselves — ID3v2 for MP3, Vorbis comments for FLAC/OGG, atoms for MP4, chunks for WAV — instead of the Windows Search index, which only covers indexed locations.
- A sidecar `.lrc` next to the audio file is matched by name.
- The last scan's snapshot paints the UI at startup; a background rescan reconciles it and keeps the current track playing.
- Scan progress advances track by track.

### Playback

- Repeat all, repeat one and shuffle.
- A play-next queue: queued tracks play before the rest, in insertion order.
- Auto-advance; unreadable files are skipped; Previous returns to the start of the track once it has played for more than 3 seconds.
- A progress bar shows the current position.

### Now playing

- Cover art comes from embedded artwork through the system thumbnail pipeline, feathered on all four edges into a background sampled from the cover.
- Audio specs: format · bit depth / sample rate · average bitrate.
- Landscape composition: cover left, perspective lyrics right; the title bar and controls surface at the window edges.

### Lyrics

- LRC line timestamps, inline word timestamps, `[tr]` translation blocks and hour-scale timestamps; stacked timestamps expand into separate lines.
- Embedded lyrics: MP3 `USLT`, FLAC/OGG `LYRICS`/`SYNCEDLYRICS`, M4A `©lyr`.
- The current line stays centred, lights up word by word and scales up, with its translated line; half-line fades at both edges; scrolling and word highlighting follow the display refresh rate.
- Encoding detection: BOM first, then strict UTF-8, falling back to GB18030.

### Queue

- Search on Enter: title, artist and album matches first, then lyric lines (original and translation).
- Sort by default order, favourites, modified time, title, artist, album or duration, ascending or descending.
- Favourites, play next, locate the playing track, refresh the library, scroll to top.

### Desktop integration

- One instance per user; launching again brings the existing window forward.
- Optional tray icon; the close button can exit or minimise to tray.
- Immersive title bar with custom caption buttons; the top bar and controls appear as the pointer approaches the edges.
- Double-click left to play or pause, right to maximise; F11 for full screen; Space and the arrow keys for transport; Esc backs out one layer at a time.
- Per-monitor V2 DPI awareness.

### Settings

- Theme and language apply immediately (system / 简体中文 / English).
- Close behaviour, lyric layout (font size, visible lines, line spacing) and music folders; the version is shown at the foot of the page.

## Tech Stack

| Layer | Technology |
| --- | --- |
| Language | C# / .NET 10 |
| UI | WinUI 3 (Windows App SDK 2.5.1) + XAML |
| Playback | Windows.Media.Playback.MediaPlayer |
| Metadata | Hand-written ID3v2, FLAC, MP4 and WAV readers; no third-party parsing library |
| Cover art | System media thumbnails + pixel-level edge feathering + palette extraction |
| Lyrics | Custom LRC parser (word timing, translations) and embedded-lyrics readers |
| Persistence | JSON under `%LOCALAPPDATA%\YiChao` |
| Distribution | Unpackaged x64; Windows App SDK self-contained, .NET runtime framework-dependent |
| Build | .NET SDK 10, dotnet CLI |

## Project Structure

```
.
├── docs/
│   └── icon.png                        # App icon (from Echo Tide)
├── src/
│   └── YiChao.App/
│       ├── data/                       # Data layer
│       │   ├── library/                #   Directory scanning and library snapshot
│       │   ├── lyrics/                 #   LRC parser, embedded lyrics and lyric cache
│       │   ├── metadata/               #   Cover loading and feathering, palette, audio specs, tags
│       │   │   └── tags/               #     ID3v2 / FLAC / MP4 / WAV tag readers
│       │   ├── model/                  #   Track model
│       │   ├── playback/               #   Playback service, play modes, play-next queue, sorting
│       │   └── settings/               #   Preferences model and settings.json I/O
│       ├── screens/                    # Screens
│       │   ├── home/                   #   Home player
│       │   │   ├── component/          #     background / bar / dialog / player / playlist
│       │   │   └── expanded/           #     Landscape assembly
│       │   └── settings/               #   Settings screen (component/entry)
│       ├── ui/                         # Globally shared UI
│       │   ├── component/              #   Cover fade, icons, caption buttons, single instance, tray
│       │   └── theme/                  #   Palette and Chinese/English strings
│       ├── App.xaml.cs                 # App entry: single instance, tray, close behaviour, crash log
│       ├── MainWindow.xaml.cs          # Window: immersive title bar, shortcuts, full screen
│       ├── YiChao.App.csproj
│       └── app.manifest
├── .gitignore
├── LICENSE
└── README.md / README.zh-CN.md
```

## Getting Started

### Requirements

- Windows 10 version 1809 (build 17763) or newer, x64 only; Windows 11 recommended.
- .NET 10 SDK to build; .NET 10 Desktop Runtime to run (the current publish is framework-dependent).

### Build

```powershell
git clone https://github.com/Evilgodxu/EchoTide-Music-WinUI.git
cd EchoTide-Music-WinUI

dotnet build src/YiChao.App/YiChao.App.csproj -c Release
```

The executable is written to `src\YiChao.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\`.

### Publish

```powershell
dotnet publish src/YiChao.App/YiChao.App.csproj -c Release -o publish/0.0.1
```

The output bundles the Windows App SDK runtime; add `-p:SelfContained=true` if the target machine should not need the .NET 10 Desktop Runtime.

## Data and Logs

- `%LOCALAPPDATA%\YiChao\settings.json` — library folders, theme, language, close behaviour, lyric layout and favourites.
- `%LOCALAPPDATA%\YiChao\library.json` — library snapshot.
- `crash.log` beside the executable — unhandled exception details.

## Status

The Windows build is still catching up with the Android version. This release covers the local library only; online search, proxy sources and metadata editing are not implemented yet and will be ported in stages.

## Acknowledgements

- The application icon is taken from the Android version of Echo Tide.

## License

[AGPL-3.0](LICENSE) © 2026 Evilgodxu
