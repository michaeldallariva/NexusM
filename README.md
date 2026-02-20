rel setup, DI, middleware
├── Dockerfile                 # Docker build for Linux
├── .dockerignore
├── Controllers/
│   └── MusicApiController.cs  # REST API endpoints
├── Data/
│   └── MusicDbContext.cs      # EF Core database context
├── Models/
│   ├── AppConfig.cs           # Configuration model
│   └── MusicModels.cs         # Track, Album, Artist, Playlist entities
├── Services/
│   ├── ConfigService.cs       # .conf file loader
│   └── LibraryScannerService.cs # Background music scanner
├── Properties/
│   └── launchSettings.json
└── wwwroot/
    ├── index.html             # SPA shell
    ├── css/
    │   └── app.css            # Dark theme stylesheet
    └── js/
        └── app.js             # Client-side SPA application
```

## Roadmap

- **Phase 1** (current): Windows - Kestrel web server, SQLite, basic web UI
- **Phase 2**: Docker/Linux deployment, mobile-responsive improvements
- **Phase 3**: User authentication, multi-user support
- **Phase 4**: Transcoding via FFmpeg, Subsonic API compatibility
Good question. FFmpeg isn't essential for a basic music library, but it becomes useful for a few specific things:
Transcoding on-the-fly — If you're streaming FLAC files (which can be 30-50MB each) to a mobile phone over cellular data, FFmpeg can convert them to MP3/AAC at a lower bitrate in real-time before sending. Navidrome and Airsonic both do this. The user hears their music without burning through their data plan.
Format compatibility — Not all browsers can play every audio format. For example, most browsers can't play .ape, .wma, or .dff (DSD) files natively. FFmpeg can transcode those to something the browser understands on the fly.
Extracting album art — Some formats embed cover art in ways that TagLibSharp can't always read. FFmpeg can extract embedded images from virtually any audio container.
Audio normalization — FFmpeg can analyze and normalize volume levels across tracks so you don't get blasted when one quiet jazz track is followed by a loud rock track.
For Phase 1 where you're mostly playing MP3/FLAC on a local network, you don't need it at all — TagLibSharp handles the metadata and browsers play MP3/FLAC/AAC natively. It only becomes relevant when you want mobile streaming over slower connections or need to support exotic formats. That's why I pushed it to Phase 4.
- **Phase 5**: Mobile app (PWA or native client)

## License

Private project - All rights reserved.
