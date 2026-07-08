# NexusM

**NexusM** is a self-hosted, portable media library manager that runs as a lightweight web server. It lets you organize, browse, and stream your personal media collection from any device on your local network through a modern web interface.

No cloud dependency. No subscription. Your media, your server, your rules.

---

## Overview

NexusM is a single-file executable (Windows and Linux) that serves a web-based UI for managing and playing multiple types of media. It scans your local and network drives, extracts metadata, fetches rich information from online databases (TMDB, TVMaze, LRCLIB, Jikan/MyAnimeList, Watchmode), and presents everything through a responsive interface accessible from any browser on your network.

---

## Features

### Media Libraries
- **Music** - Browse by tracks, albums, artists, and genres. ID3/Vorbis tag extraction, album artwork, lyrics display with synced lyrics support (via LRCLIB). FTS5 full-text search, ReplayGain volume normalisation, and M3U playlist export.
- **Movies and TV Shows** - Poster grid with metadata from TMDB/TVMaze. Cast photos, descriptions, genres, watched status, and resume playback.
- **Anime** - Dedicated anime library powered by Jikan (MyAnimeList). Series and episode browsing with MAL ratings, synopsis, cover art, and character data.
- **Actors** - Browse actors from your video library. Biography, filmography, and "Known For" credits from TMDB, with clickable cast on movie and TV detail pages.
- **Music Videos** - Artist-based browsing with auto-generated thumbnails.
- **Pictures** - Photo gallery with category filtering, EXIF metadata, and thumbnail generation.
- **eBooks** - PDF and EPUB library with cover extraction and in-browser reader.
- **Comic Books** - CBZ and CBR archives with a dedicated page-by-page reader.
- **AudioBooks** - MP3 and M4B audiobook library with per-book progress tracking, chapter navigation, variable playback speed, sleep timer, bookmarks, and a "Continue Listening" shelf.
- **Podcasts** - RSS feed subscriptions with episode playback, OPML import, and a built-in Discover panel.
- **Radio** - Internet radio stations with country and genre filtering, live streaming, and real-time ICY/Icecast stream title updates.
- **Internet TV** - IPTV channel management via M3U/M3U8 playlist import. Includes an EPG programme guide (XMLTV) with a Plex-style timeline grid, now/next data, and automatic channel matching.

### Playback
- Built-in web audio player with shuffle, repeat, queue management, and lyrics overlay.
- 10-band graphic equalizer with presets, applied via Web Audio API. Works for music, radio, and audiobooks.
- Audio output device picker - route playback to a specific local output (speakers, monitor, Bluetooth headset) directly from the player bar, the browser equivalent of a native app's output selector.
- Built-in web video player with HLS streaming and a redesigned cinema-style interface.
- Google Cast - cast music, movies, TV, and music videos from the web interface to any Google TV or Chromecast on your network. Server-side casting (like DLNA) works over plain HTTP with no browser HTTPS requirement, with album art on screen, playlist auto-advance, and a device picker.
- Chapters - movies, TV, anime, and documentaries with embedded chapters show a chapter list and let you jump between them during playback.
- Server-side video transcoding with hardware acceleration: NVIDIA NVENC, Intel QSV/VAAPI, and AMD AMF.
- HDR video support - tone mapping pipeline with dual-track HLS (HDR passthrough + SDR fallback). Supports HDR10, HDR10+, HLG, and Dolby Vision via zscale and tonemapx.
- Automatic remuxing for format compatibility without re-encoding.
- HLS caching to avoid re-transcoding previously watched content.
- Subtitle support - automatic online search and download via OpenSubtitles.com and SubDL.com, plus automatic sidecar .srt detection (Jellyfin-compatible naming), manual SRT/VTT upload per title, embedded subtitle tracks, and a client-side sync-offset control to fix out-of-sync subtitles.
- Video resume - saves and restores playback position per user, per title.
- Where to Watch - displays streaming availability for movies and TV shows via Watchmode, showing which services currently carry the title in your region.
- Community Ratings - TMDB community scores shown on movie and TV detail pages.
- Trakt.tv integration - native support for Trakt scrobbling, syncing watched history, and accessing your Trakt watchlist and ratings directly from NexusM.
- Last.fm scrobbling - scrobble your music listening history to Last.fm.

### Discovery
- **What's New Online** - a discovery page showing recent and upcoming releases from TMDB across movies, TV shows, anime, documentaries, and cartoons. Filter by country, genre, and mode (recent or upcoming). Streaming provider badges are shown for each title where available.
- **fanart.tv Artwork** - pick alternative posters, backdrops, and artist images from fanart.tv in the metadata editor for movies, TV shows, and music artists.
- **Mood Explorer** - 8 mood-based music recommendation filters: Chill, Energy, Focus, Party, Love, Study, Discovery, and Nostalgia.
- **Go Big Mode** - a fullscreen TV and cinema presentation mode with large poster cards, full keyboard navigation (arrow keys, Enter, Escape, F for fullscreen, Space to pause), and a paired mobile remote. A phone or tablet on the same network can browse the library and send content to the main display while Go Big is running.

### Playlists
- **Auto-Generated Playlists** - automatically builds smart playlists from your music library: by decade, top 100 most played, recently added, and all favourites. Configuration is saved server-side for consistency across all devices.
- Manual playlists with drag-and-drop track ordering and per-user storage.

### Analytics and Insights
- **Deep Dive Library Analysis** - detailed breakdowns of your library by file format, video codec, resolution, audio codec, genre, bitrate, and release year. Separate charts for music, movies, TV, and other media types with exportable data.
- Viewing insights: watch patterns over time, top genres, completion rates, comfort rewatch titles, and listening statistics.
- Smart Insights - highlights from your library such as hidden gems, most-rewatched titles, and recently discovered artists.
- **Server Resource Monitor** - real-time CPU and RAM usage graphs updated every 15 seconds, visible from the admin panel. Cross-platform: reads /proc/stat and /proc/meminfo on Linux, uses P/Invoke on Windows.
- **Library Scan Scheduler** - automated background scanning on a configurable schedule, so your library stays up to date without manual intervention.
- CSV and HTML export of full library data.

### Network Sharing
- **DLNA/UPnP Media Server** - built-in DLNA server with SSDP device discovery. Exposes your music, videos, music videos, and anime library to any DLNA-compatible renderer on the local network (smart TVs, VLC, Kodi, hardware media players). No third-party DLNA software required.
- Network share (SMB/CIFS) support with credential management and auto-mount on startup.

### Security and Multi-User
- PIN-based authentication with admin and guest roles.
- IP whitelist for network-level access control.
- Per-user favourites, playlists, watched history, play counts, and preferences.
- Configurable session timeout.
- HTTPS support with auto-generated self-signed certificate including all LAN IPs and mDNS hostname.
- Access logging - separate rotating logs for LAN (LocalAccess.log) and remote (RemoteAccess.log) media access, recording user, IP, timestamp, media, and status.

### Customisation
- 6 built-in visual themes: Dark, Blue, Purple, Emerald, Sky Grey, and Sky Blue.
- **Custom Template System** - load external CSS and JavaScript template files at runtime to completely restyle the interface without touching the core application. Templates are hot-loaded from the templates folder and selectable from Settings. Ships with 6 templates: Horizon (amber home-theatre layout), Amazing! (Amazon Prime style), Jellyfish (Jellyfin inspired), YablakoTV (Apple TV style), Plux! (Plex inspired), and Netflux (Netflix style, with hero video previews).
- Configurable sidebar with individual show/hide toggles for every section.
- UI preferences including default view modes, Go Big autostart, and per-section sort order.
- 26 interface languages: English, French, German, Spanish, Portuguese, Italian, Dutch, Polish, Romanian, Russian, Swedish, Ukrainian, Norwegian, Finnish, Estonian, Lithuanian, Slovenian, Albanian, Serbian, Chinese, Japanese, Korean, Hindi, Indonesian, Vietnamese, and Thai.

### Other
- Global search across all media types with instant results.
- Auto-update - optional, opt-in in-app updater that checks nexusm.org, downloads new versions, verifies checksums, backs up the current install, and applies with one click, with rollback support.
- System tray integration and optional Windows startup registration.
- Portable - no installation required, runs from a single folder on Windows or Linux.

---

## Technology Stack

| Component | Technology |
|---|---|
| Runtime | .NET 8 (ASP.NET Core, Kestrel) |
| Database | SQLite via Entity Framework Core |
| Frontend | Vanilla JavaScript, HTML5, CSS3 (no framework) |
| Video Streaming | HLS via hls.js, FFmpeg for transcoding and remuxing |
| HDR Tone Mapping | FFmpeg tonemapx, zscale (libzimg), VAAPI, D3D11VA, NVDEC |
| eBook and Comic Rendering | epub.js, PDF.js, SharpCompress (CBZ/CBR) |
| Metadata | TMDB API, TVMaze API, LRCLIB API, Jikan API (MyAnimeList), fanart.tv API |
| Streaming Providers | Watchmode API |
| Scrobbling | Trakt.tv API, Last.fm API |
| EPG | XMLTV parsing |
| Google Cast | Sharpcaster (CASTV2 protocol) |
| DLNA/UPnP | Custom SSDP implementation (System.Net.Sockets, no external package) |
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
- FFmpeg (Custom version, built-in)
- TMDB API key (optional, free - for movie/TV metadata, actor data, and What's New Online)
- Watchmode API key (optional, free tier available - for streaming provider availability)

### Running NexusM

1. Extract the portable package to a folder of your choice.
2. Run `NexusM.exe` on Windows or `./NexusM` on Linux.
3. A browser window will open to `http://localhost:8182`.
4. Log in with the default username `admin` and PIN `000000` on first use.
5. Go to **Settings** to configure your media library folders, API keys, and other preferences.
6. Click **Scan Library** to index your media.

### Accessing from Other Devices

NexusM binds to all network interfaces by default (`0.0.0.0:8182`). Access it from any device on your network using the host machine's IP address (e.g., `http://192.168.1.100:8182`). Ensure your firewall allows connections on port 8182.

---

## Configuration

NexusM is configured via `NexusM.conf` (INI format) located next to the executable. Most settings can also be changed from the web-based Settings page.

Key configuration sections: Server, Security, Library, Playback, Transcoding, Metadata, Database, Logging, DLNA, and UI.

See the [User Guide](https://nexusm.org/d/nexusm_user_guide.pdf) for a full configuration reference.

---

## License

This project is licensed under **Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International (CC BY-NC-ND 4.0)**.

You are free to use and share NexusM for personal, non-commercial purposes. Commercial use, modification, and redistribution of modified versions are not permitted.

See [LICENSE](LICENSE) for full terms.

## Third-Party Libraries

NexusM uses open-source libraries under their respective licenses (MIT, Apache-2.0, LGPL-2.1, BSD-2-Clause, Unlicense). See [LICENSE](LICENSE) for the complete list.

---
<img width="1877" height="1031" alt="Image" src="https://github.com/user-attachments/assets/d9a22d4d-7681-4d85-86ee-b55744bc6c2b" />

<img width="1877" height="1024" alt="Image" src="https://github.com/user-attachments/assets/304a6148-7648-4115-aa30-9ba5727365c1" />

<img width="2500" height="1367" alt="Image" src="https://github.com/user-attachments/assets/32f94ee8-e281-425a-9c76-70504c0cdb83" />

<img width="1655" height="693" alt="Image" src="https://github.com/user-attachments/assets/991bd589-c5de-4801-a0ce-c99425f65993" />

<img width="1877" height="1386" alt="Image" src="https://github.com/user-attachments/assets/3617d976-121b-494b-aa50-daab4c7976e3" />

<img width="2506" height="1331" alt="Image" src="https://github.com/user-attachments/assets/e17367ac-7046-406b-a357-e0fc9aa65d73" />

---
Android client / Google TV

<img width="1280" height="800" alt="Image" src="https://github.com/user-attachments/assets/3776b2b9-0044-4e39-8f4d-2414af6625b9" />
<img width="1280" height="800" alt="Image" src="https://github.com/user-attachments/assets/3825b52b-ed55-4b20-a21d-cafce9dc14ba" />

