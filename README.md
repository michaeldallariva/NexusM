# NexusM

**NexusM** is a self-hosted, portable media library manager that runs as a lightweight web server. It lets you organize, browse, and stream your personal media collection from any device on your local network through a modern web interface.

No cloud dependency. No subscription. Your media, your server, your rules.

---

## Overview

NexusM is a single-file Windows executable that serves a web-based UI for managing and playing multiple types of media. It scans your local and network drives, extracts metadata, fetches rich information from online databases (TMDB, TVMaze, LRCLIB), and presents everything through a responsive interface accessible from any browser.

---

## Features

### Media Libraries
- **Music** — Browse by tracks, albums, artists, and genres. ID3/Vorbis tag extraction, album artwork, lyrics display with synced lyrics support (via LRCLIB).
- **Movies & TV Shows** — Poster grid with metadata from TMDB/TVMaze. Cast photos, descriptions, genres, watched status, resume playback.
- **Actors** — Browse actors from your video library. Biography, filmography, "Known For" credits from TMDB, clickable cast in movie/TV detail views.
- **Music Videos** — Artist-based browsing with auto-generated thumbnails.
- **Pictures** — Photo gallery with category filtering, EXIF metadata, and thumbnail generation.
- **eBooks** — PDF and EPUB library with cover extraction and in-browser reader.
- **Comic Books** — CBZ and CBR archives with a dedicated page-by-page reader.
- **AudioBooks** — MP3 and M4B audiobook library with per-book progress tracking.
- **Podcasts** — RSS feed subscriptions with episode playback, OPML import, and a built-in Discover panel.
- **Radio** — Internet radio stations with country/genre filtering, live streaming, and real-time ICY/Icecast stream title updates.
- **Internet TV** — IPTV channel management via M3U/M3U8 playlist import.

### Playback
- Built-in web audio player with shuffle, repeat, queue management, and lyrics overlay.
- **10-band graphic equalizer** with presets, applied via Web Audio API — works for music, radio, and audiobooks.
- Built-in web video player with HLS streaming.
- Server-side video transcoding with hardware acceleration (NVIDIA NVENC, Intel QSV, AMD AMF).
- Automatic remuxing for format compatibility without re-encoding.
- HLS caching to avoid re-transcoding previously watched content.
- **Subtitle support** — automatic subtitle search and download via OpenSubtitles.com and SubDL.com.
- Video resume — saves and restores playback position across sessions.

### Metadata
- Automatic metadata fetching from TMDB (movies, TV, actors) and TVMaze (TV shows).
- Synced and plain-text lyrics from LRCLIB.
- Cast and crew information with downloadable actor photos.
- Album artwork extraction from audio file tags.

### Analytics & Discovery
- Library analysis with format, codec, resolution, genre, and bitrate distribution charts.
- Viewing insights: watch patterns, top genres, completion stats, comfort titles.
- **Mood Explorer** — 8 mood-based recommendation cards (Chill, Energy, Focus, Party, Love, Study, Discovery, Nostalgia).
- CSV and HTML export of library data.

### Security & Multi-User
- PIN-based authentication with admin and guest roles.
- IP whitelist for network access control.
- Per-user favourites, playlists, and play counts.
- Configurable session timeout.

### Other
- Global search across all media types.
- Network share (SMB/CIFS) support with credential management and auto-mount.
- 6 visual themes: Dark, Blue, Purple, Emerald, Sky Grey, Sky Blue.
- 17+ languages: English, French, German, Spanish, Portuguese, Italian, Dutch, Polish, Romanian, Russian, Swedish, Ukrainian, Norwegian, Finnish, Estonian, Lithuanian, Slovenian, Albanian, Serbian.
- Configurable sidebar with show/hide toggles for each section.
- System tray integration and optional Windows startup registration.
- Portable — no installation required, runs from a single folder.

---

## Technology Stack

| Component | Technology |
|---|---|
| Runtime | .NET 8 (ASP.NET Core, Kestrel) |
| Database | SQLite via Entity Framework Core |
| Frontend | Vanilla JavaScript, HTML5, CSS3 (no framework) |
| Video Streaming | HLS via hls.js, FFmpeg for transcoding/remuxing |
| eBook / Comic Rendering | epub.js, PDF.js, SharpCompress (CBZ/CBR) |
| Metadata | TMDB API, TVMaze API, LRCLIB API |
| Audio Tags | TagLibSharp |
| Image Processing | SixLabors.ImageSharp |
| PDF Processing | PdfSharpCore, PDFtoImage |
| Logging | Serilog |
| Configuration | INI-based (ini-parser-netstandard) |

---

## Getting Started

### Requirements
- Windows 10/11 (64-bit) or Linux (x64)
- A modern web browser (Chrome, Firefox, Edge, Safari)
- FFmpeg (optional, for audio/video transcoding — can be auto-downloaded from within the app)
- TMDB API key (optional, free — for movie/TV metadata and actor data)

### Running NexusM

1. Extract the portable package to a folder of your choice.
2. Run `NexusM.exe`.
3. A browser window will open to `http://localhost:8182`.
4. Log in with the default username `admin` and set a PIN on first use (000000 is the default one)
5. Go to **Settings** to configure your media library folders, TMDB API key, and other preferences.
6. Click **Scan Library** to index your media.

### Accessing from Other Devices

NexusM binds to all network interfaces by default (`0.0.0.0:8182`). Access it from any device on your network using the host machine's IP address (e.g., `http://192.168.1.100:8182`). Ensure Windows Firewall allows the connection.

---

## Configuration

NexusM is configured via `NexusM.conf` (INI format) located next to the executable. Most settings can also be changed from the web-based Settings page.

Key configuration sections: Server, Security, Library, Playback, Transcoding, Metadata, Database, Logging, and UI.

See the [User Guide](https://nexusm.org/d/nexusm_user_guide.pdf) for a full configuration reference.

---

## License

This project is licensed under **Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International (CC BY-NC-ND 4.0)**.

You are free to use and share NexusM for personal, non-commercial purposes. Commercial use, modification, and redistribution of modified versions are not permitted.

See [LICENSE](LICENSE) for full terms.

## Third-Party Libraries

NexusM uses open-source libraries under their respective licenses (MIT, Apache-2.0, LGPL-2.1, BSD-2-Clause, Unlicense). See [LICENSE](LICENSE) for the complete list.

---

![Image](https://github.com/user-attachments/assets/3c4d404c-8993-480e-8713-b378d5cc13ad)
![Image](https://github.com/user-attachments/assets/159ff5df-2309-4c91-a75e-7eec8ee0038e)
![Image](https://github.com/user-attachments/assets/273f2f67-4d2e-49b2-8928-3e3da2b8cb7d)
![Image](https://github.com/user-attachments/assets/66986c22-e1f0-4388-87c0-c51efea31f71)
![Image](https://github.com/user-attachments/assets/ae8f5628-2120-4c70-a4c6-9bae49186c31)
![Image](https://github.com/user-attachments/assets/99163f59-da13-441c-be1b-fdd504ad3b4f)


