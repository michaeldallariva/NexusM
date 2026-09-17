using System.Diagnostics;
using System.Net;
using System.Security;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;
using NexusM.Services;

namespace NexusM.Controllers;

/// <summary>
/// DLNA/UPnP MediaServer HTTP endpoints.
///
/// Exposes:
///   GET  /dlna/device.xml                  - UPnP device description
///   GET  /dlna/contentdirectory/desc.xml   - ContentDirectory service description
///   POST /dlna/contentdirectory/control    - Browse / GetSearchCapabilities / GetSortCapabilities / GetSystemUpdateID
///   GET  /dlna/connectionmanager/desc.xml  - ConnectionManager service description
///   POST /dlna/connectionmanager/control   - GetProtocolInfo / GetCurrentConnectionIDs
///   GET  /dlna/stream/track/{id}           - Audio file (direct serve, range-enabled)
///   GET  /dlna/stream/video/{id}           - Video: direct MP4 or FFmpeg audio-remux proxy
///   GET  /dlna/stream/mv/{id}              - Music video: direct MP4 or FFmpeg audio-remux proxy
///   GET  /dlna/art/track/{id}              - Album art for a track
///   GET  /dlna/art/video/{id}              - Poster for a video
///
/// All endpoints are [AllowAnonymous] - DLNA does not authenticate.
/// Access is restricted to LAN (private/loopback) IP addresses.
/// </summary>
[ApiController]
[Route("dlna")]
[AllowAnonymous]
public class DlnaController : ControllerBase
{
    private readonly MusicDbContext _db;
    private readonly VideosDbContext _videoDb;
    private readonly MusicVideosDbContext _mvDb;
    private readonly FFmpegService _ffmpeg;
    private readonly ConfigService _config;
    private readonly ILogger<DlnaController> _logger;

    // DLNA flags: streaming, no-RTSP, no-DLNA-timeline, no-byte-range metadata (standard)
    private const string DlnaFlags = "01700000000000000000000000000000";

    // Audio codecs considered DLNA-compatible for MP4 direct serve
    private static readonly HashSet<string> DlnaCompatAudio = new(StringComparer.OrdinalIgnoreCase)
        { "aac", "mp3", "mp2", "mp2a", "ac3", "pcm_s16le", "pcm_s24le", "pcm_s16be", "" };

    public DlnaController(
        MusicDbContext db,
        VideosDbContext videoDb,
        MusicVideosDbContext mvDb,
        FFmpegService ffmpeg,
        ConfigService config,
        ILogger<DlnaController> logger)
    {
        _db = db;
        _videoDb = videoDb;
        _mvDb = mvDb;
        _ffmpeg = ffmpeg;
        _config = config;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  GUARD HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private bool DlnaEnabled => _config.Config.Dlna.Enabled;
    private string FriendlyName => _config.Config.Dlna.FriendlyName;
    private string Udn => DlnaService.Udn;
    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    /// <summary>
    /// True when the request's REAL origin is on the LAN. Cloudflare Tunnel and the NexusM
    /// Relay deliver internet traffic over loopback, so the raw connection address is not
    /// enough - NetGuard recovers the origin from CF-Connecting-IP / X-Forwarded-For (trusted
    /// only from a loopback peer) exactly as the Access Gate does.
    /// </summary>
    private bool IsLanRequest() => NetGuard.IsLanRequest(HttpContext);

    private ContentResult? Guard()
    {
        if (!DlnaEnabled) return Content("DLNA disabled", "text/plain");
        if (!IsLanRequest()) return Content("LAN access only", "text/plain");
        return null;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DEVICE DESCRIPTION
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("device.xml")]
    public IActionResult DeviceDescription()
    {
        if (Guard() is { } err) return StatusCode(403, err);

        var xml = $"""
            <?xml version="1.0"?>
            <root xmlns="urn:schemas-upnp-org:device-1-0" xmlns:dlna="urn:schemas-dlna-org:device-1-0">
              <specVersion><major>1</major><minor>0</minor></specVersion>
              <device>
                <deviceType>urn:schemas-upnp-org:device:MediaServer:1</deviceType>
                <friendlyName>{SecurityElement.Escape(FriendlyName)}</friendlyName>
                <manufacturer>NexusM</manufacturer>
                <modelDescription>NexusM Self-Hosted Media Server</modelDescription>
                <modelName>NexusM</modelName>
                <modelNumber>2026.05</modelNumber>
                <serialNumber>1</serialNumber>
                <UDN>uuid:{Udn}</UDN>
                <dlna:X_DLNADOC xmlns:dlna="urn:schemas-dlna-org:device-1-0">DMS-1.50</dlna:X_DLNADOC>
                <iconList>
                  <icon>
                    <mimetype>image/png</mimetype>
                    <width>48</width><height>48</height><depth>24</depth>
                    <url>/dlna/icon/48</url>
                  </icon>
                  <icon>
                    <mimetype>image/png</mimetype>
                    <width>192</width><height>192</height><depth>24</depth>
                    <url>/dlna/icon/192</url>
                  </icon>
                  <icon>
                    <mimetype>image/png</mimetype>
                    <width>512</width><height>512</height><depth>24</depth>
                    <url>/dlna/icon/512</url>
                  </icon>
                </iconList>
                <serviceList>
                  <service>
                    <serviceType>urn:schemas-upnp-org:service:ContentDirectory:1</serviceType>
                    <serviceId>urn:upnp-org:serviceId:ContentDirectory</serviceId>
                    <SCPDURL>/dlna/contentdirectory/desc.xml</SCPDURL>
                    <controlURL>/dlna/contentdirectory/control</controlURL>
                    <eventSubURL>/dlna/contentdirectory/events</eventSubURL>
                  </service>
                  <service>
                    <serviceType>urn:schemas-upnp-org:service:ConnectionManager:1</serviceType>
                    <serviceId>urn:upnp-org:serviceId:ConnectionManager</serviceId>
                    <SCPDURL>/dlna/connectionmanager/desc.xml</SCPDURL>
                    <controlURL>/dlna/connectionmanager/control</controlURL>
                    <eventSubURL>/dlna/connectionmanager/events</eventSubURL>
                  </service>
                </serviceList>
                <presentationURL>/</presentationURL>
              </device>
            </root>
            """;

        return Content(xml, "text/xml; charset=\"utf-8\"");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CONTENTDIRECTORY SERVICE DESCRIPTION (SCPD)
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("contentdirectory/desc.xml")]
    public IActionResult ContentDirectoryDescription()
    {
        if (Guard() is { } err) return StatusCode(403, err);
        return Content(ContentDirectoryScpd, "text/xml; charset=\"utf-8\"");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CONTENTDIRECTORY CONTROL (SOAP)
    // ══════════════════════════════════════════════════════════════════════════

    [HttpPost("contentdirectory/control")]
    public async Task<IActionResult> ContentDirectoryControl()
    {
        if (Guard() is { } err) return StatusCode(403, err);

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        // SOAPAction: "urn:schemas-upnp-org:service:ContentDirectory:1#Browse"
        var soapAction = Request.Headers["SOAPAction"].ToString().Trim('"');
        var action = soapAction.Contains('#')
            ? soapAction[(soapAction.LastIndexOf('#') + 1)..]
            : soapAction;

        return action switch
        {
            "Browse" => await HandleBrowse(body),
            "GetSearchCapabilities" => SoapResponse("ContentDirectory:1", "GetSearchCapabilitiesResponse",
                "<SearchCaps></SearchCaps>"),
            "GetSortCapabilities" => SoapResponse("ContentDirectory:1", "GetSortCapabilitiesResponse",
                "<SortCaps></SortCaps>"),
            "GetSystemUpdateID" => SoapResponse("ContentDirectory:1", "GetSystemUpdateIDResponse",
                "<Id>1</Id>"),
            _ => SoapFault($"Unknown action: {action}")
        };
    }

    private async Task<IActionResult> HandleBrowse(string body)
    {
        // Parse SOAP body
        string objectId = "", browseFlag = "BrowseDirectChildren";
        int startIndex = 0, requestedCount = 0;

        try
        {
            var doc = XDocument.Parse(body);
            XNamespace u = "urn:schemas-upnp-org:service:ContentDirectory:1";
            var browse = doc.Descendants(u + "Browse").FirstOrDefault()
                      ?? doc.Descendants("Browse").FirstOrDefault();
            if (browse != null)
            {
                objectId       = browse.Element(u + "ObjectID")?.Value ?? browse.Element("ObjectID")?.Value ?? "0";
                browseFlag     = browse.Element(u + "BrowseFlag")?.Value ?? browse.Element("BrowseFlag")?.Value ?? "BrowseDirectChildren";
                startIndex     = int.TryParse(browse.Element(u + "StartingIndex")?.Value ?? browse.Element("StartingIndex")?.Value, out var si) ? si : 0;
                requestedCount = int.TryParse(browse.Element(u + "RequestedCount")?.Value ?? browse.Element("RequestedCount")?.Value, out var rc) ? rc : 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DLNA: Failed to parse Browse SOAP body");
            return SoapFault("Invalid SOAP body");
        }

        _logger.LogDebug("DLNA Browse: ObjectID={ObjId} Flag={Flag} Start={Start} Count={Count}",
            objectId, browseFlag, startIndex, requestedCount);

        var base64 = objectId.Length > 0 ? objectId : "0";

        // BrowseMetadata: return the object itself
        if (browseFlag == "BrowseMetadata")
            return await BrowseMetadata(objectId);

        // BrowseDirectChildren
        return await BrowseDirectChildren(objectId, startIndex, requestedCount);
    }

    private async Task<IActionResult> BrowseMetadata(string objectId)
    {
        var sb = new StringBuilder();
        BeginDidl(sb);

        if (objectId == "0")
        {
            AppendContainer(sb, "0", "-1", "NexusM Library", "object.container", 3);
        }
        else if (objectId == "music")
        {
            AppendContainer(sb, "music", "0", "Music", "object.container", 3);
        }
        else if (objectId == "videos")
        {
            AppendContainer(sb, "videos", "0", "Videos", "object.container", 3);
        }
        else if (objectId == "musicvideos")
        {
            AppendContainer(sb, "musicvideos", "0", "Music Videos", "object.container", -1);
        }
        else
        {
            // For item-level metadata, fall back to BrowseDirectChildren of parent
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), 0, 0);
        }

        EndDidl(sb);
        return BuildBrowseResponse(sb.ToString(), 1, 1);
    }

    private async Task<IActionResult> BrowseDirectChildren(string objectId, int start, int count)
    {
        var limit = count == 0 ? int.MaxValue : count;

        // ── Root ──────────────────────────────────────────────────────────────
        if (objectId == "0")
        {
            var sb = new StringBuilder();
            BeginDidl(sb);
            var items = new List<(string id, string title)>();
            if (_config.Config.UI.ShowMusic)       items.Add(("music", "Music"));
            items.Add(("videos", "Videos"));   // Movies / TV Shows / Documentaries always exposed over DLNA
            if (_config.Config.UI.ShowMusicVideos) items.Add(("musicvideos", "Music Videos"));
            if (_config.Config.UI.ShowAnime)       items.Add(("anime", "Anime"));

            var page = items.Skip(start).Take(limit).ToList();
            foreach (var (id, title) in page)
                AppendContainer(sb, id, "0", title, "object.container", -1);
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), page.Count, items.Count);
        }

        // ── Music root ────────────────────────────────────────────────────────
        if (objectId == "music")
        {
            var sb = new StringBuilder();
            BeginDidl(sb);
            var containers = new[]
            {
                ("music_tracks",  "All Tracks"),
                ("music_albums",  "Albums"),
                ("music_artists", "Artists"),
            };
            var page = containers.Skip(start).Take(limit).ToList();
            foreach (var (id, title) in page)
                AppendContainer(sb, id, "music", title, "object.container", -1);
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), page.Count, containers.Length);
        }

        // ── All tracks ────────────────────────────────────────────────────────
        if (objectId == "music_tracks")
        {
            var query = _db.Tracks
                .OrderBy(t => t.AlbumArtist ?? t.Artist)
                .ThenBy(t => t.Album)
                .ThenBy(t => t.DiscNumber ?? 1)
                .ThenBy(t => t.TrackNumber ?? 0)
                .ThenBy(t => t.Title);
            var total = await query.CountAsync();
            var tracks = await query.Skip(start).Take(limit).ToListAsync();

            var sb = new StringBuilder();
            BeginDidl(sb);
            foreach (var t in tracks)
                AppendTrackItem(sb, t, "music_tracks");
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), tracks.Count, total);
        }

        // ── Albums list ───────────────────────────────────────────────────────
        if (objectId == "music_albums")
        {
            var albums = await _db.Tracks
                .GroupBy(t => new { Album = t.Album, Artist = t.AlbumArtist ?? t.Artist })
                .Select(g => new { g.Key.Album, g.Key.Artist, Count = g.Count() })
                .OrderBy(a => a.Artist).ThenBy(a => a.Album)
                .ToListAsync();

            var page = albums.Skip(start).Take(limit).ToList();
            var sb = new StringBuilder();
            BeginDidl(sb);
            foreach (var a in page)
            {
                var encId = "music_album_" + B64Encode(a.Album + "\x1F" + a.Artist);
                AppendContainer(sb, encId, "music_albums",
                    string.IsNullOrEmpty(a.Artist) ? a.Album : $"{a.Album} - {a.Artist}",
                    "object.container.album.musicAlbum", a.Count);
            }
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), page.Count, albums.Count);
        }

        // ── Tracks in album ───────────────────────────────────────────────────
        if (objectId.StartsWith("music_album_"))
        {
            var data = B64Decode(objectId["music_album_".Length..]).Split('\x1F', 2);
            var albumTitle  = data[0];
            var albumArtist = data.Length > 1 ? data[1] : "";

            var query = _db.Tracks
                .Where(t => t.Album == albumTitle &&
                            (t.AlbumArtist == albumArtist || (t.AlbumArtist == null && t.Artist == albumArtist)))
                .OrderBy(t => t.DiscNumber ?? 1)
                .ThenBy(t => t.TrackNumber ?? 0)
                .ThenBy(t => t.Title);
            var total = await query.CountAsync();
            var tracks = await query.Skip(start).Take(limit).ToListAsync();

            var sb = new StringBuilder();
            BeginDidl(sb);
            foreach (var t in tracks)
                AppendTrackItem(sb, t, objectId);
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), tracks.Count, total);
        }

        // ── Artists list ──────────────────────────────────────────────────────
        if (objectId == "music_artists")
        {
            var artists = await _db.Tracks
                .Select(t => t.AlbumArtist != null && t.AlbumArtist != "" ? t.AlbumArtist : t.Artist)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();

            var page = artists.Skip(start).Take(limit).ToList();
            var sb = new StringBuilder();
            BeginDidl(sb);
            foreach (var a in page)
            {
                var encId = "music_artist_" + B64Encode(a);
                AppendContainer(sb, encId, "music_artists", a, "object.container.person.musicArtist", -1);
            }
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), page.Count, artists.Count);
        }

        // ── Tracks by artist ──────────────────────────────────────────────────
        if (objectId.StartsWith("music_artist_"))
        {
            var artist = B64Decode(objectId["music_artist_".Length..]);
            var query = _db.Tracks
                .Where(t => t.Artist == artist || t.AlbumArtist == artist)
                .OrderBy(t => t.Album)
                .ThenBy(t => t.DiscNumber ?? 1)
                .ThenBy(t => t.TrackNumber ?? 0)
                .ThenBy(t => t.Title);
            var total = await query.CountAsync();
            var tracks = await query.Skip(start).Take(limit).ToListAsync();

            var sb = new StringBuilder();
            BeginDidl(sb);
            foreach (var t in tracks)
                AppendTrackItem(sb, t, objectId);
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), tracks.Count, total);
        }

        // ── Videos root ───────────────────────────────────────────────────────
        if (objectId == "videos")
        {
            var sb = new StringBuilder();
            BeginDidl(sb);
            var containers = new[]
            {
                ("videos_movies", "Movies"),
                ("videos_tv",     "TV Shows"),
                ("videos_docs",   "Documentaries"),
            };
            var page = containers.Skip(start).Take(limit).ToList();
            foreach (var (id, title) in page)
                AppendContainer(sb, id, "videos", title, "object.container", -1);
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), page.Count, containers.Length);
        }

        // ── Movies ────────────────────────────────────────────────────────────
        if (objectId == "videos_movies")
        {
            var query = _videoDb.Videos
                .Where(v => v.MediaType == "movie")
                .OrderBy(v => v.Title);
            var total = await query.CountAsync();
            var videos = await query.Skip(start).Take(limit).ToListAsync();

            var sb = new StringBuilder();
            BeginDidl(sb);
            foreach (var v in videos)
                AppendVideoItem(sb, v, "videos_movies", "object.item.videoItem.movie");
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), videos.Count, total);
        }

        // ── TV shows list ─────────────────────────────────────────────────────
        if (objectId == "videos_tv")
        {
            var series = await _videoDb.Videos
                .Where(v => v.MediaType == "tv" && v.SeriesName != null && v.SeriesName != "")
                .Select(v => v.SeriesName)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            var page = series.Skip(start).Take(limit).ToList();
            var sb = new StringBuilder();
            BeginDidl(sb);
            foreach (var s in page)
            {
                var encId = "videos_show_" + B64Encode(s ?? "");
                AppendContainer(sb, encId, "videos_tv", s ?? "", "object.container", -1);
            }
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), page.Count, series.Count);
        }

        // ── Episodes in TV show ───────────────────────────────────────────────
        if (objectId.StartsWith("videos_show_"))
        {
            var seriesName = B64Decode(objectId["videos_show_".Length..]);
            var query = _videoDb.Videos
                .Where(v => v.SeriesName == seriesName)
                .OrderBy(v => v.Season ?? 0)
                .ThenBy(v => v.Episode ?? 0)
                .ThenBy(v => v.Title);
            var total = await query.CountAsync();
            var episodes = await query.Skip(start).Take(limit).ToListAsync();

            var sb = new StringBuilder();
            BeginDidl(sb);
            foreach (var v in episodes)
                AppendVideoItem(sb, v, objectId, "object.item.videoItem");
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), episodes.Count, total);
        }

        // ── Documentaries ─────────────────────────────────────────────────────
        // Doc series appear as sub-containers (episodes served by the shared
        // "videos_show_" handler above); standalone docs appear as items.
        if (objectId == "videos_docs")
        {
            var series = await _videoDb.Videos
                .Where(v => v.MediaType == "documentary" && v.SeriesName != null && v.SeriesName != "")
                .Select(v => v.SeriesName)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            var standalone = await _videoDb.Videos
                .Where(v => v.MediaType == "documentary" && (v.SeriesName == null || v.SeriesName == ""))
                .OrderBy(v => v.Title)
                .ToListAsync();

            var total = series.Count + standalone.Count;

            var sb = new StringBuilder();
            BeginDidl(sb);
            // Page across the combined [series…, standalone…] sequence.
            int idx = 0, emitted = 0;
            foreach (var s in series)
            {
                if (idx >= start && emitted < limit)
                {
                    var encId = "videos_show_" + B64Encode(s ?? "");
                    AppendContainer(sb, encId, "videos_docs", s ?? "", "object.container", -1);
                    emitted++;
                }
                idx++;
            }
            foreach (var v in standalone)
            {
                if (idx >= start && emitted < limit)
                {
                    AppendVideoItem(sb, v, "videos_docs", "object.item.videoItem");
                    emitted++;
                }
                idx++;
            }
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), emitted, total);
        }

        // ── Music Videos ──────────────────────────────────────────────────────
        if (objectId == "musicvideos")
        {
            var query = _mvDb.MusicVideos.OrderBy(v => v.Artist).ThenBy(v => v.Title);
            var total = await query.CountAsync();
            var mvs = await query.Skip(start).Take(limit).ToListAsync();

            var sb = new StringBuilder();
            BeginDidl(sb);
            foreach (var mv in mvs)
                AppendMusicVideoItem(sb, mv, "musicvideos");
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), mvs.Count, total);
        }

        // ── Anime (reuses videos table with MediaType="anime") ────────────────
        if (objectId == "anime")
        {
            var query = _videoDb.Videos
                .Where(v => v.MediaType == "anime")
                .OrderBy(v => v.SeriesName)
                .ThenBy(v => v.Season ?? 0)
                .ThenBy(v => v.Episode ?? 0);
            var total = await query.CountAsync();
            var items = await query.Skip(start).Take(limit).ToListAsync();

            var sb = new StringBuilder();
            BeginDidl(sb);
            foreach (var v in items)
                AppendVideoItem(sb, v, "anime", "object.item.videoItem");
            EndDidl(sb);
            return BuildBrowseResponse(sb.ToString(), items.Count, total);
        }

        // Unknown object
        return BuildBrowseResponse("<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" " +
            "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
            "xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\"></DIDL-Lite>", 0, 0);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CONNECTIONMANAGER
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("connectionmanager/desc.xml")]
    public IActionResult ConnectionManagerDescription()
    {
        if (Guard() is { } err) return StatusCode(403, err);
        return Content(ConnectionManagerScpd, "text/xml; charset=\"utf-8\"");
    }

    [HttpPost("connectionmanager/control")]
    public IActionResult ConnectionManagerControl()
    {
        if (Guard() is { } err) return StatusCode(403, err);

        var soapAction = Request.Headers["SOAPAction"].ToString().Trim('"');
        var action = soapAction.Contains('#') ? soapAction[(soapAction.LastIndexOf('#') + 1)..] : soapAction;

        return action switch
        {
            "GetProtocolInfo" => SoapResponse("ConnectionManager:1", "GetProtocolInfoResponse",
                $"<Source>{SecurityElement.Escape(ProtocolInfoSource)}</Source><Sink></Sink>"),
            "GetCurrentConnectionIDs" => SoapResponse("ConnectionManager:1", "GetCurrentConnectionIDsResponse",
                "<ConnectionIDs>0</ConnectionIDs>"),
            "GetCurrentConnectionInfo" => SoapResponse("ConnectionManager:1", "GetCurrentConnectionInfoResponse",
                "<RcsID>-1</RcsID><AVTransportID>-1</AVTransportID>" +
                "<ProtocolInfo></ProtocolInfo><PeerConnectionManager></PeerConnectionManager>" +
                "<PeerConnectionID>-1</PeerConnectionID><Direction>Output</Direction><Status>OK</Status>"),
            _ => SoapFault($"Unknown action: {action}")
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  STREAM ENDPOINTS
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Serve an audio track. Always direct-serve with range support.</summary>
    [HttpGet("stream/track/{id:int}")]
    public async Task<IActionResult> StreamTrack(int id)
    {
        if (!DlnaEnabled || !IsLanRequest()) return NotFound();

        var track = await _db.Tracks.FindAsync(id);
        if (track == null || !System.IO.File.Exists(track.FilePath)) return NotFound();

        var mime = NormaliseMime(track.MimeType, track.FilePath);
        Response.Headers["transferMode.dlna.org"] = "Streaming";
        Response.Headers["contentFeatures.dlna.org"] =
            $"DLNA.ORG_PN={AudioDlnaProfile(mime)};DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}";
        return PhysicalFile(track.FilePath, mime, enableRangeProcessing: true);
    }

    /// <summary>
    /// Serve a video file directly. Uses the file's native container (MP4 or MKV) so the
    /// client reads the real duration from the file header and byte-range seeking works.
    /// VLC supports MKV and all common codecs natively - no FFmpeg proxy needed.
    /// </summary>
    [HttpGet("stream/video/{id:int}")]
    public async Task<IActionResult> StreamVideo(int id, CancellationToken ct)
    {
        if (!DlnaEnabled || !IsLanRequest()) return NotFound();

        var video = await _videoDb.Videos.FindAsync(new object[] { id }, ct);
        if (video == null || !System.IO.File.Exists(video.FilePath)) return NotFound();

        Response.Headers["transferMode.dlna.org"] = "Streaming";

        // Serve the native file - VLC/most clients handle MKV and MP4 natively.
        // Byte-range (OP=01) works because PhysicalFile handles Range headers automatically.
        // The client reads duration directly from the file's moov/MKV header (avoids
        // the empty_moov pipe bug where duration is always 0).
        var mime = GetVideoMime(video.FilePath);
        var pn = IsVideoDirectServable(video) ? "DLNA.ORG_PN=AVC_MP4_HP_HD_AAC;" : "";
        Response.Headers["contentFeatures.dlna.org"] =
            $"{pn}DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}";
        return PhysicalFile(video.FilePath, mime, enableRangeProcessing: true);
    }

    /// <summary>Serve a music video. Direct MP4 when compliant (byte-range seek); otherwise FFmpeg proxy (time-seek).</summary>
    [HttpGet("stream/mv/{id:int}")]
    public async Task<IActionResult> StreamMusicVideo(int id, CancellationToken ct)
    {
        if (!DlnaEnabled || !IsLanRequest()) return NotFound();

        var mv = await _mvDb.MusicVideos.FindAsync(new object[] { id }, ct);
        if (mv == null || !System.IO.File.Exists(mv.FilePath)) return NotFound();

        Response.Headers["transferMode.dlna.org"] = "Streaming";

        if (mv.Mp4Compliant && mv.AudioChannels <= 6)
        {
            Response.Headers["contentFeatures.dlna.org"] =
                $"DLNA.ORG_PN=AVC_MP4_HP_HD_AAC;DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}";
            return PhysicalFile(mv.FilePath, "video/mp4", enableRangeProcessing: true);
        }

        var seekSecs = ParseNptTime(Request.Headers["TimeSeekRange.dlna.org"]);
        return await RunFfmpegProxy(mv.FilePath, mv.Duration, seekSecs, copyAudio: false, ct);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ART ENDPOINTS  (anonymous, LAN-only)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Server icon for the device description &lt;iconList&gt; (the official NexusM
    /// PWA icons under wwwroot). Roku Media Player requires a server icon to list
    /// the DMS. Anonymous + LAN-only, like the other DLNA endpoints.
    /// </summary>
    [HttpGet("icon/{size:int}")]
    public IActionResult DeviceIcon(int size)
    {
        if (!DlnaEnabled || !IsLanRequest()) return NotFound();
        // Only the sizes advertised in device.xml are served.
        if (size != 48 && size != 192 && size != 512) return NotFound();
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot", $"icon-{size}.png");
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "image/png");
    }

    [HttpGet("art/track/{id:int}")]
    public async Task<IActionResult> TrackArt(int id)
    {
        if (!DlnaEnabled || !IsLanRequest()) return NotFound();
        var track = await _db.Tracks.FindAsync(id);
        if (track?.AlbumArtCached == null) return NotFound();
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "albumart", track.AlbumArtCached);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "image/jpeg");
    }

    [HttpGet("art/video/{id:int}")]
    public async Task<IActionResult> VideoArt(int id)
    {
        if (!DlnaEnabled || !IsLanRequest()) return NotFound();
        var video = await _videoDb.Videos.FindAsync(id);
        if (video?.PosterPath == null) return NotFound();
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "videometa", video.PosterPath);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "image/jpeg");
    }

    [HttpGet("art/mv/{id:int}")]
    public async Task<IActionResult> MusicVideoArt(int id)
    {
        if (!DlnaEnabled || !IsLanRequest()) return NotFound();
        var mv = await _mvDb.MusicVideos.FindAsync(id);
        if (mv?.ThumbnailPath == null) return NotFound();
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "mvthumbs", mv.ThumbnailPath);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "image/jpeg");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  FFMPEG PROXY  (audio remux → fragmented MP4 pipe)
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IActionResult> RunFfmpegProxy(
        string filePath, double duration, double seekSecs, bool copyAudio, CancellationToken ct)
    {
        if (_ffmpeg.FfmpegPath == null)
            return StatusCode(503, "FFmpeg not available for DLNA stream proxy");

        // -ss before -i = fast input seek (uses index, doesn't decode discarded frames)
        var seekArg = seekSecs > 0 ? $"-ss {seekSecs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} " : "";
        var audioArg = copyAudio ? "-c:a copy" : "-c:a aac -ac 2 -b:a 192k";

        var args = $"-hide_banner -loglevel error " +
                   $"{seekArg}-i \"{filePath}\" " +
                   $"-map 0:v:0 -map 0:a:0 " +
                   $"-c:v copy {audioArg} " +
                   $"-f mp4 -movflags frag_keyframe+empty_moov " +
                   $"pipe:1";

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpeg.FfmpegPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Response.ContentType = "video/mp4";
        // OP=10: time-seek supported (TimeSeekRange.dlna.org), byte-range not supported (pipe)
        Response.Headers["contentFeatures.dlna.org"] =
            $"DLNA.ORG_PN=AVC_MP4_HP_HD_AAC;DLNA.ORG_OP=10;DLNA.ORG_FLAGS={DlnaFlags}";
        Response.Headers["Cache-Control"] = "no-cache";
        // Tell the client the current seek range so it can draw the progress bar
        if (duration > 0)
        {
            var end = duration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            var start = seekSecs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["TimeSeekRange.dlna.org"] = $"npt={start}-{end}/{end}";
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(Response.Body, ct);
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        finally
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync(CancellationToken.None);
        }

        return new EmptyResult();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DIDL-LITE BUILDERS
    // ══════════════════════════════════════════════════════════════════════════

    private static void BeginDidl(StringBuilder sb)
    {
        sb.Append("<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" " +
                  "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
                  "xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">");
    }

    private static void EndDidl(StringBuilder sb) => sb.Append("</DIDL-Lite>");

    private static void AppendContainer(StringBuilder sb, string id, string parentId,
        string title, string upnpClass, int childCount)
    {
        var cc = childCount >= 0 ? $" childCount=\"{childCount}\"" : "";
        sb.Append($"<container id=\"{X(id)}\" parentID=\"{X(parentId)}\" restricted=\"1\"{cc}>");
        sb.Append($"<dc:title>{X(title)}</dc:title>");
        sb.Append($"<upnp:class>{upnpClass}</upnp:class>");
        sb.Append("</container>");
    }

    /// <summary>
    /// Emit cover/thumbnail art for an item: both a upnp:albumArtURI (with the
    /// DLNA JPEG_TN profile hint) and a secondary image/jpeg &lt;res&gt;. Some
    /// renderers paint a thumbnail only from albumArtURI, others only from a
    /// JPEG_TN &lt;res&gt;, so we provide both. (VLC's UPnP tree ignores both and
    /// shows a generic icon - that is a VLC limitation, not a server issue.)
    /// </summary>
    private static void AppendArt(StringBuilder sb, string artUrl)
    {
        var u = X(artUrl);
        sb.Append($"<upnp:albumArtURI dlna:profileID=\"JPEG_TN\" xmlns:dlna=\"urn:schemas-dlna-org:device-1-0\">{u}</upnp:albumArtURI>");
        sb.Append($"<res protocolInfo=\"http-get:*:image/jpeg:DLNA.ORG_PN=JPEG_TN;DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}\">{u}</res>");
    }

    private void AppendTrackItem(StringBuilder sb, Track t, string parentId)
    {
        var mime = NormaliseMime(t.MimeType, t.FilePath);
        var profile = AudioDlnaProfile(mime);
        var protocolInfo = $"http-get:*:{mime}:DLNA.ORG_PN={profile};DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}";
        var streamUrl = $"{BaseUrl}/dlna/stream/track/{t.Id}";
        var dur = FormatDuration(t.Duration);

        sb.Append($"<item id=\"track_{t.Id}\" parentID=\"{X(parentId)}\" restricted=\"1\">");
        sb.Append($"<dc:title>{X(t.Title)}</dc:title>");
        sb.Append("<upnp:class>object.item.audioItem.musicTrack</upnp:class>");
        if (!string.IsNullOrEmpty(t.Artist))
        {
            sb.Append($"<dc:creator>{X(t.Artist)}</dc:creator>");
            sb.Append($"<upnp:artist>{X(t.Artist)}</upnp:artist>");
        }
        if (!string.IsNullOrEmpty(t.Album))
            sb.Append($"<upnp:album>{X(t.Album)}</upnp:album>");
        if (t.TrackNumber.HasValue)
            sb.Append($"<upnp:originalTrackNumber>{t.TrackNumber}</upnp:originalTrackNumber>");
        if (!string.IsNullOrEmpty(t.Genre))
            sb.Append($"<upnp:genre>{X(t.Genre)}</upnp:genre>");
        if (t.Year.HasValue)
            sb.Append($"<dc:date>{t.Year}-01-01</dc:date>");
        if (!string.IsNullOrEmpty(t.AlbumArtCached))
            AppendArt(sb, $"{BaseUrl}/dlna/art/track/{t.Id}");
        sb.Append($"<res protocolInfo=\"{X(protocolInfo)}\" duration=\"{dur}\" size=\"{t.FileSize}\">" +
                  $"{X(streamUrl)}</res>");
        sb.Append("</item>");
    }

    private void AppendVideoItem(StringBuilder sb, Video v, string parentId, string upnpClass)
    {
        // All videos use byte-range seek (OP=01) - served directly from disk via PhysicalFile.
        var mime = GetVideoMime(v.FilePath);
        var pn = IsVideoDirectServable(v) ? "DLNA.ORG_PN=AVC_MP4_HP_HD_AAC;" : "";
        var protocolInfo = $"http-get:*:{mime}:{pn}DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}";
        var streamUrl = $"{BaseUrl}/dlna/stream/video/{v.Id}";
        var dur = FormatDuration(v.Duration);

        var label = v.MediaType == "tv" && v.Season.HasValue && v.Episode.HasValue
            ? $"{v.SeriesName} S{v.Season:D2}E{v.Episode:D2} {v.Title}".Trim()
            : v.Title;

        sb.Append($"<item id=\"video_{v.Id}\" parentID=\"{X(parentId)}\" restricted=\"1\">");
        sb.Append($"<dc:title>{X(label)}</dc:title>");
        sb.Append($"<upnp:class>{upnpClass}</upnp:class>");
        if (!string.IsNullOrEmpty(v.Genre))   sb.Append($"<upnp:genre>{X(v.Genre)}</upnp:genre>");
        if (v.Year.HasValue)                  sb.Append($"<dc:date>{v.Year}-01-01</dc:date>");
        if (!string.IsNullOrEmpty(v.Overview)) sb.Append($"<dc:description>{X(v.Overview[..Math.Min(v.Overview.Length, 256)])}</dc:description>");
        if (!string.IsNullOrEmpty(v.PosterPath))
            AppendArt(sb, $"{BaseUrl}/dlna/art/video/{v.Id}");
        var res = $"<res protocolInfo=\"{X(protocolInfo)}\" duration=\"{dur}\" size=\"{v.SizeBytes}\"";
        if (v.Width > 0 && v.Height > 0) res += $" resolution=\"{v.Width}x{v.Height}\"";
        res += $">{X(streamUrl)}</res>";
        sb.Append(res);
        sb.Append("</item>");
    }

    private void AppendMusicVideoItem(StringBuilder sb, MusicVideo mv, string parentId)
    {
        var direct = mv.Mp4Compliant && mv.AudioChannels <= 6;
        var op = direct ? "01" : "10";
        var protocolInfo = $"http-get:*:video/mp4:DLNA.ORG_PN=AVC_MP4_HP_HD_AAC;DLNA.ORG_OP={op};DLNA.ORG_FLAGS={DlnaFlags}";
        var streamUrl = $"{BaseUrl}/dlna/stream/mv/{mv.Id}";
        var dur = FormatDuration(mv.Duration);

        sb.Append($"<item id=\"mv_{mv.Id}\" parentID=\"{X(parentId)}\" restricted=\"1\">");
        sb.Append($"<dc:title>{X(mv.Title)}</dc:title>");
        sb.Append("<upnp:class>object.item.videoItem.musicVideoClip</upnp:class>");
        if (!string.IsNullOrEmpty(mv.Artist))
        {
            sb.Append($"<dc:creator>{X(mv.Artist)}</dc:creator>");
            sb.Append($"<upnp:artist>{X(mv.Artist)}</upnp:artist>");
        }
        if (!string.IsNullOrEmpty(mv.Genre)) sb.Append($"<upnp:genre>{X(mv.Genre)}</upnp:genre>");
        if (!string.IsNullOrEmpty(mv.ThumbnailPath))
            AppendArt(sb, $"{BaseUrl}/dlna/art/mv/{mv.Id}");
        var res = $"<res protocolInfo=\"{X(protocolInfo)}\" duration=\"{dur}\" size=\"{mv.SizeBytes}\"";
        if (mv.Width > 0 && mv.Height > 0) res += $" resolution=\"{mv.Width}x{mv.Height}\"";
        res += $">{X(streamUrl)}</res>";
        sb.Append(res);
        sb.Append("</item>");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SOAP HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private IActionResult BuildBrowseResponse(string didl, int returned, int total)
    {
        var escaped = SecurityElement.Escape(didl)!;
        var soap = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"
                        s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
              <s:Body>
                <u:BrowseResponse xmlns:u="urn:schemas-upnp-org:service:ContentDirectory:1">
                  <Result>{escaped}</Result>
                  <NumberReturned>{returned}</NumberReturned>
                  <TotalMatches>{total}</TotalMatches>
                  <UpdateID>1</UpdateID>
                </u:BrowseResponse>
              </s:Body>
            </s:Envelope>
            """;
        return Content(soap, "text/xml; charset=\"utf-8\"");
    }

    private static IActionResult SoapResponse(string service, string responseElement, string innerXml)
    {
        var soap = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"
                        s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
              <s:Body>
                <u:{responseElement} xmlns:u="urn:schemas-upnp-org:service:{service}">
                  {innerXml}
                </u:{responseElement}>
              </s:Body>
            </s:Envelope>
            """;
        return new ContentResult { Content = soap, ContentType = "text/xml; charset=\"utf-8\"", StatusCode = 200 };
    }

    private static IActionResult SoapFault(string description) =>
        new ContentResult
        {
            Content = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
                  <s:Body>
                    <s:Fault>
                      <faultcode>s:Client</faultcode>
                      <faultstring>UPnPError</faultstring>
                      <detail>
                        <UPnPError xmlns="urn:schemas-upnp-org:control-1-0">
                          <errorCode>401</errorCode>
                          <errorDescription>{SecurityElement.Escape(description)}</errorDescription>
                        </UPnPError>
                      </detail>
                    </s:Fault>
                  </s:Body>
                </s:Envelope>
                """,
            ContentType = "text/xml; charset=\"utf-8\"",
            StatusCode = 500
        };

    // ══════════════════════════════════════════════════════════════════════════
    //  UTILITY
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>XML-escape a string for use in element content or attribute values.</summary>
    private static string X(string? s) => SecurityElement.Escape(s ?? "") ?? "";

    private static string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }

    private static string B64Encode(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
               .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string B64Decode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(padded)); }
        catch { return ""; }
    }

    private static bool IsVideoDirectServable(Video v) =>
        v.Mp4Compliant && DlnaCompatAudio.Contains(v.AudioCodec ?? "");

    /// <summary>Returns the correct MIME type for a video file based on its extension.</summary>
    private static string GetVideoMime(string? filePath) =>
        Path.GetExtension(filePath ?? "").ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".mkv"           => "video/x-matroska",
            ".avi"           => "video/avi",
            ".mov"           => "video/quicktime",
            ".wmv"           => "video/x-ms-wmv",
            ".ts" or ".m2ts" => "video/mp2t",
            _                => "video/mp4"
        };

    /// <summary>
    /// Parse a TimeSeekRange.dlna.org header value into seconds.
    /// Accepts "npt=120.5-", "npt=00:02:00.500-", "npt=00:02:00-", etc.
    /// Returns 0 if the header is absent or unparseable.
    /// </summary>
    private static double ParseNptTime(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return 0;

        // Strip "npt=" prefix (case-insensitive)
        var s = header.Trim();
        if (s.StartsWith("npt=", StringComparison.OrdinalIgnoreCase))
            s = s["npt=".Length..];

        // Take the start time only (before the first '-')
        var dashIdx = s.IndexOf('-');
        if (dashIdx >= 0) s = s[..dashIdx];
        s = s.Trim();

        if (string.IsNullOrEmpty(s)) return 0;

        // Plain seconds: "120" or "120.5"
        if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var secs))
            return secs;

        // HH:MM:SS or HH:MM:SS.mmm
        if (TimeSpan.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var ts))
            return ts.TotalSeconds;

        return 0;
    }

    /// <summary>Ensure MIME type is valid; fall back to extension-based guess.</summary>
    private static string NormaliseMime(string? mime, string filePath)
    {
        if (!string.IsNullOrWhiteSpace(mime) && mime.Contains('/')) return mime;
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".mp3"  => "audio/mpeg",
            ".flac" => "audio/flac",
            ".m4a" or ".aac" => "audio/mp4",
            ".ogg"  => "audio/ogg",
            ".wav"  => "audio/wav",
            ".wma"  => "audio/x-ms-wma",
            ".opus" => "audio/ogg",
            ".ape"  => "audio/x-ape",
            ".aiff" or ".aif" => "audio/aiff",
            _       => "audio/mpeg"
        };
    }

    private static string AudioDlnaProfile(string mime) => mime switch
    {
        "audio/mpeg"     => "MP3",
        "audio/flac"     => "FLAC",
        "audio/mp4"      => "AAC_ISO",
        "audio/wav"      => "LPCM",
        "audio/x-ms-wma" => "WMABASE",
        _                => "MP3"  // generic fallback
    };

    // ══════════════════════════════════════════════════════════════════════════
    //  PROTOCOL INFO  (ConnectionManager)
    // ══════════════════════════════════════════════════════════════════════════

    private static readonly string ProtocolInfoSource = string.Join(',', new[]
    {
        $"http-get:*:audio/mpeg:DLNA.ORG_PN=MP3;DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}",
        $"http-get:*:audio/flac:DLNA.ORG_PN=FLAC;DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}",
        $"http-get:*:audio/mp4:DLNA.ORG_PN=AAC_ISO;DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}",
        $"http-get:*:audio/ogg:*:DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}",
        $"http-get:*:audio/wav:DLNA.ORG_PN=LPCM;DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}",
        $"http-get:*:audio/x-ms-wma:DLNA.ORG_PN=WMABASE;DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}",
        $"http-get:*:video/mp4:DLNA.ORG_PN=AVC_MP4_HP_HD_AAC;DLNA.ORG_OP=01;DLNA.ORG_FLAGS={DlnaFlags}",
    });

    // ══════════════════════════════════════════════════════════════════════════
    //  SCPD XML STRINGS
    // ══════════════════════════════════════════════════════════════════════════

    private const string ContentDirectoryScpd = """
        <?xml version="1.0"?>
        <scpd xmlns="urn:schemas-upnp-org:service-1-0">
          <specVersion><major>1</major><minor>0</minor></specVersion>
          <actionList>
            <action>
              <name>Browse</name>
              <argumentList>
                <argument><name>ObjectID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_ObjectID</relatedStateVariable></argument>
                <argument><name>BrowseFlag</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_BrowseFlag</relatedStateVariable></argument>
                <argument><name>Filter</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_Filter</relatedStateVariable></argument>
                <argument><name>StartingIndex</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_Index</relatedStateVariable></argument>
                <argument><name>RequestedCount</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_Count</relatedStateVariable></argument>
                <argument><name>SortCriteria</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_SortCriteria</relatedStateVariable></argument>
                <argument><name>Result</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_Result</relatedStateVariable></argument>
                <argument><name>NumberReturned</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_Count</relatedStateVariable></argument>
                <argument><name>TotalMatches</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_Count</relatedStateVariable></argument>
                <argument><name>UpdateID</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_UpdateID</relatedStateVariable></argument>
              </argumentList>
            </action>
            <action>
              <name>GetSearchCapabilities</name>
              <argumentList>
                <argument><name>SearchCaps</name><direction>out</direction><relatedStateVariable>SearchCapabilities</relatedStateVariable></argument>
              </argumentList>
            </action>
            <action>
              <name>GetSortCapabilities</name>
              <argumentList>
                <argument><name>SortCaps</name><direction>out</direction><relatedStateVariable>SortCapabilities</relatedStateVariable></argument>
              </argumentList>
            </action>
            <action>
              <name>GetSystemUpdateID</name>
              <argumentList>
                <argument><name>Id</name><direction>out</direction><relatedStateVariable>SystemUpdateID</relatedStateVariable></argument>
              </argumentList>
            </action>
          </actionList>
          <serviceStateTable>
            <stateVariable><name>A_ARG_TYPE_ObjectID</name><sendEventsAttribute>no</sendEventsAttribute><dataType>string</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_BrowseFlag</name><sendEventsAttribute>no</sendEventsAttribute><dataType>string</dataType>
              <allowedValueList><allowedValue>BrowseMetadata</allowedValue><allowedValue>BrowseDirectChildren</allowedValue></allowedValueList>
            </stateVariable>
            <stateVariable><name>A_ARG_TYPE_Filter</name><sendEventsAttribute>no</sendEventsAttribute><dataType>string</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_Result</name><sendEventsAttribute>no</sendEventsAttribute><dataType>string</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_Index</name><sendEventsAttribute>no</sendEventsAttribute><dataType>ui4</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_Count</name><sendEventsAttribute>no</sendEventsAttribute><dataType>ui4</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_UpdateID</name><sendEventsAttribute>no</sendEventsAttribute><dataType>ui4</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_SortCriteria</name><sendEventsAttribute>no</sendEventsAttribute><dataType>string</dataType></stateVariable>
            <stateVariable><name>SearchCapabilities</name><sendEventsAttribute>no</sendEventsAttribute><dataType>string</dataType></stateVariable>
            <stateVariable><name>SortCapabilities</name><sendEventsAttribute>no</sendEventsAttribute><dataType>string</dataType></stateVariable>
            <stateVariable sendEvents="yes"><name>SystemUpdateID</name><sendEventsAttribute>yes</sendEventsAttribute><dataType>ui4</dataType></stateVariable>
          </serviceStateTable>
        </scpd>
        """;

    private const string ConnectionManagerScpd = """
        <?xml version="1.0"?>
        <scpd xmlns="urn:schemas-upnp-org:service-1-0">
          <specVersion><major>1</major><minor>0</minor></specVersion>
          <actionList>
            <action>
              <name>GetProtocolInfo</name>
              <argumentList>
                <argument><name>Source</name><direction>out</direction><relatedStateVariable>SourceProtocolInfo</relatedStateVariable></argument>
                <argument><name>Sink</name><direction>out</direction><relatedStateVariable>SinkProtocolInfo</relatedStateVariable></argument>
              </argumentList>
            </action>
            <action>
              <name>GetCurrentConnectionIDs</name>
              <argumentList>
                <argument><name>ConnectionIDs</name><direction>out</direction><relatedStateVariable>CurrentConnectionIDs</relatedStateVariable></argument>
              </argumentList>
            </action>
            <action>
              <name>GetCurrentConnectionInfo</name>
              <argumentList>
                <argument><name>ConnectionID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_ConnectionID</relatedStateVariable></argument>
                <argument><name>RcsID</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_RcsID</relatedStateVariable></argument>
                <argument><name>AVTransportID</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_AVTransportID</relatedStateVariable></argument>
                <argument><name>ProtocolInfo</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_ProtocolInfo</relatedStateVariable></argument>
                <argument><name>PeerConnectionManager</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_ConnectionManager</relatedStateVariable></argument>
                <argument><name>PeerConnectionID</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_ConnectionID</relatedStateVariable></argument>
                <argument><name>Direction</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_Direction</relatedStateVariable></argument>
                <argument><name>Status</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_ConnectionStatus</relatedStateVariable></argument>
              </argumentList>
            </action>
          </actionList>
          <serviceStateTable>
            <stateVariable sendEvents="yes"><name>SourceProtocolInfo</name><sendEventsAttribute>yes</sendEventsAttribute><dataType>string</dataType></stateVariable>
            <stateVariable sendEvents="yes"><name>SinkProtocolInfo</name><sendEventsAttribute>yes</sendEventsAttribute><dataType>string</dataType></stateVariable>
            <stateVariable sendEvents="yes"><name>CurrentConnectionIDs</name><sendEventsAttribute>yes</sendEventsAttribute><dataType>string</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_ConnectionID</name><sendEventsAttribute>no</sendEventsAttribute><dataType>i4</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_RcsID</name><sendEventsAttribute>no</sendEventsAttribute><dataType>i4</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_AVTransportID</name><sendEventsAttribute>no</sendEventsAttribute><dataType>i4</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_ProtocolInfo</name><sendEventsAttribute>no</sendEventsAttribute><dataType>string</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_ConnectionManager</name><sendEventsAttribute>no</sendEventsAttribute><dataType>string</dataType></stateVariable>
            <stateVariable><name>A_ARG_TYPE_Direction</name><sendEventsAttribute>no</sendEventsAttribute><dataType>string</dataType><allowedValueList><allowedValue>Input</allowedValue><allowedValue>Output</allowedValue></allowedValueList></stateVariable>
            <stateVariable><name>A_ARG_TYPE_ConnectionStatus</name><sendEventsAttribute>no</sendEventsAttribute><dataType>string</dataType><allowedValueList><allowedValue>OK</allowedValue><allowedValue>ContentFormatMismatch</allowedValue><allowedValue>InsufficientBandwidth</allowedValue><allowedValue>UnreliableChannel</allowedValue><allowedValue>Unknown</allowedValue></allowedValueList></stateVariable>
          </serviceStateTable>
        </scpd>
        """;
}
