# Changelog / Release Notes

## Android App 2.8

**Status:** Released 24-July-2026

### Improvements

- Remote-control seeking has been redesigned. Pressing **LEFT** or **RIGHT** now seeks by 5% of the video's total duration, with a minimum jump of 10 seconds.
- Repeated or held remote-control presses now accumulate, allowing users to move rapidly through long videos and jump approximately 30 minutes within a few presses.
- Seek amounts longer than 60 seconds are now displayed in a more readable format, such as `3m` or `1h 5m`, instead of showing the total number of seconds.
- The on-screen rewind and fast-forward buttons now display the amount of time that will be skipped.
- Remote navigation within the video player has been improved. Users can now move correctly between the close button, seek bar, transport controls, and secondary control icons using the **UP** and **DOWN** buttons.
- Partially watched episodes now display playback-progress bars on the series detail page.
- The Continue Watching card now displays a playback-progress bar and the remaining viewing time.

### Fixes

- Fixed a remote-control seeking issue that prevented repeated or held **LEFT** and **RIGHT** presses from accumulating correctly.
- Fixed playback-progress reports incorrectly using the player-reported duration, which could corrupt the watched status of videos across every client connected to the same account.
- Fixed movies not sending their server-side runtime when reporting playback progress. Previously, only television episodes included this information.
- Reduced the resume threshold from 90% to 80% so that it matches the server's watched-status threshold.
- Fixed the seek indicator not appearing when seeking with the remote while the player controls were visible.
- Fixed pressing **DOWN** on a player control hiding the entire control bar and making the lower control rows inaccessible with the remote.

---

## NexusM Stable 2026.22

**Platforms:** Windows, Linux and Docker  
**Status:** Released 24-July-2026

### Web UI and Backend

#### Improvements

- The folder browser used by the setup wizard and Settings page now includes scrolling and a quick-filter search, preventing installations containing thousands of folders from overflowing the window.
- The **Up Next** prompt now appears 90 seconds before an episode ends instead of appearing at the final second.
- The **Up Next** prompt now includes a live countdown based on the video's actual remaining time.
- Movie and television detail pages now display runtime and resolution as colored badges matching the existing rating badges.
- Movie and television detail pages now include an audio-codec badge displaying information such as `EAC3 · 5.1`.
- Music-video detail pages now display resolution and audio-codec badges, increasing the total number of information badges from two to four.
- Videos with a resolution of `2560×1440` are now correctly identified as `1440p` instead of `1080p`.
- Movies and television episodes are now marked as watched only after at least 80% of their actual runtime has been viewed.
- Green watched-status check badges have been added to television-series and documentary episode cards in the Web UI. Previously, these badges were only displayed for movies.
- The Previous Episode and Next Episode controls have been redesigned:
  - Removed the large surrounding boxes.
  - Retained the text labels.
  - Doubled the arrow-icon size for easier touchscreen use.
- Previous Episode and Next Episode controls are now available directly inside the television episode player, removing the need to scroll down the page to switch episodes.
- The television-series page now includes a smart **Resume**, **Play Next**, or **Watch Again** button based on the user's viewing progress.
- Partially watched episodes now display playback-progress bars on the television-series page.
- Episode cards now display audio, language, and subtitle badges.
- A new **Transcoding** section has been added to the Analysis page. It displays statistics for the ten most recently transcoded videos.
- External MPV player support has been added.
- A new **MPV** button beside the Download button on supported video-detail pages generates a ready-to-use streaming link.
- MPV links can be:
  - Copied directly.
  - Downloaded as an `.m3u` file.
  - Downloaded as a `.strm` file.
  - Opened through an `mpv://` link.
- External-player links use secure, automatically expiring tokens, allowing them to work while PIN security is enabled.
- MPV playback can directly stream the original media file, including HEVC, MKV, and HDR content, without transcoding or quality loss.
- An optional H.264 transcoding fallback is available for external-player links.
- Generated external-player links include the video's correct title.
- `GET /api/videos` now returns a progress map containing each video's resume position and watched percentage.
- Added `GET /api/videos/watched/stale`, which lists videos marked as watched without corresponding real playback history.
- Added `POST /api/videos/watched/clear`, which allows selected watched-status flags to be cleared.
- Added **Settings > Library > Watched History**, providing a review-and-clear tool for incorrect watched flags created by older NexusM versions.

#### Fixes

- Fixed the **Up Next** prompt appearing too early when a video was being transcoded.
- Fixed a missing image or thumbnail in the **Up Next** prompt.
- Fixed playback progress being calculated from the player's reported duration.
- Fixed HLS transcodes reporting a shorter duration than the original video and causing content to be marked as watched after only a few minutes.
- The 80% watched threshold is now calculated using the video's real library runtime instead of the duration of the transcoded stream.
- Fixed the television-series Resume button occasionally selecting an episode that was already marked as watched.
- Resolution tiers and audio-label formatting are now handled by shared logic across detail pages and episode cards, preventing inconsistent labels.
- Fixed a login and interface flashing loop that could occur when PIN security was enabled or disabled while the server was running.
- The automatic-login state now updates immediately after changing PIN security and no longer requires a server restart.
